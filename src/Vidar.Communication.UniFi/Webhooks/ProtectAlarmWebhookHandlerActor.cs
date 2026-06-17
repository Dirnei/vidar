using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;
using Vidar.Core.Webhooks;

namespace Vidar.Communication.UniFi.Webhooks;

public sealed class ProtectAlarmWebhookHandlerActor : ReceiveActor
{
    private sealed record FetchedPayload(Guid PayloadId, string? Body, string? Error);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _webhookRegistry;
    private readonly string _hostUrl;
    private readonly Dictionary<string, Guid> _configuredDevices;

    private static readonly HttpClient PayloadClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Props Props(string hostUrl, Dictionary<string, Guid> configuredDevices) =>
        Akka.Actor.Props.Create(() =>
            new ProtectAlarmWebhookHandlerActor(hostUrl, configuredDevices));

    public ProtectAlarmWebhookHandlerActor(string hostUrl, Dictionary<string, Guid> configuredDevices)
    {
        var actorRegistry = ActorRegistry.For(Context.System);
        _shardProxy = actorRegistry.Get<DeviceTwinRegion>();
        _webhookRegistry = actorRegistry.Get<WebhookRegistry>();
        _hostUrl = hostUrl;
        _configuredDevices = configuredDevices;

        Receive<WebhookReceived>(msg => FetchPayloadAsync(msg).PipeTo(Self));
        Receive<FetchedPayload>(Handle);
    }

    private async Task<FetchedPayload> FetchPayloadAsync(WebhookReceived msg)
    {
        try
        {
            using var response = await PayloadClient.GetAsync($"{_hostUrl}/api/webhooks/payloads/{msg.PayloadId}");
            if (!response.IsSuccessStatusCode)
                return new FetchedPayload(msg.PayloadId, null, $"HTTP {(int)response.StatusCode}");
            var body = await response.Content.ReadAsStringAsync();
            return new FetchedPayload(msg.PayloadId, body, null);
        }
        catch (Exception ex)
        {
            return new FetchedPayload(msg.PayloadId, null, ex.Message);
        }
    }

    private void Handle(FetchedPayload msg)
    {
        if (msg.Body == null)
        {
            Acknowledge(msg.PayloadId, WebhookHandleStatus.Failed, msg.Error ?? "Payload fetch failed");
            return;
        }

        var alarm = ProtectAlarmWebhookParser.Parse(msg.Body);
        if (alarm == null)
        {
            Acknowledge(msg.PayloadId, WebhookHandleStatus.Failed, "Unparseable protect payload");
            return;
        }

        _log.Info("Protect alarm '{Alarm}' at {Time}: {Triggers}",
            alarm.AlarmName, alarm.Timestamp,
            string.Join("; ", alarm.Triggers.Select(t => $"{t.Key}={t.Value} (device {t.Device})")));

        foreach (var trigger in alarm.Triggers)
        {
            if (string.IsNullOrEmpty(trigger.Device)) continue;

            var nativeId = $"protect-{trigger.Device}";
            if (!_configuredDevices.TryGetValue(nativeId, out var deviceId)) continue;

            _shardProxy.Tell(new DeviceStateUpdate(deviceId, "alarm", alarm.AlarmName));
            _shardProxy.Tell(new DeviceStateUpdate(deviceId, "alarmTrigger", trigger.Key));
            if (trigger.Value != null)
                _shardProxy.Tell(new DeviceStateUpdate(deviceId, "alarmValue", trigger.Value));
        }

        Acknowledge(msg.PayloadId, WebhookHandleStatus.Handled, null);
    }

    private void Acknowledge(Guid payloadId, WebhookHandleStatus status, string? error) =>
        _webhookRegistry.Tell(new WebhookHandled(payloadId, status, error, DateTimeOffset.UtcNow));
}
