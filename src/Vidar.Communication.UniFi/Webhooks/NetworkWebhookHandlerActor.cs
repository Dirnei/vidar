using Akka.Actor;
using Akka.Event;
using Vidar.Core.Messages;

namespace Vidar.Communication.UniFi.Webhooks;

public sealed class NetworkWebhookHandlerActor : ReceiveActor
{
    private sealed record FetchedPayload(Guid PayloadId, string? Body, string? Error);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _webhookRegistry;
    private readonly string _hostUrl;
    private readonly Dictionary<string, Guid> _configuredDevices;

    private static readonly HttpClient PayloadClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Props Props(
        IActorRef shardProxy, IActorRef webhookRegistry, string hostUrl,
        Dictionary<string, Guid> configuredDevices) =>
        Akka.Actor.Props.Create(() =>
            new NetworkWebhookHandlerActor(shardProxy, webhookRegistry, hostUrl, configuredDevices));

    public NetworkWebhookHandlerActor(
        IActorRef shardProxy, IActorRef webhookRegistry, string hostUrl,
        Dictionary<string, Guid> configuredDevices)
    {
        _shardProxy = shardProxy;
        _webhookRegistry = webhookRegistry;
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

        var evt = NetworkWebhookParser.Parse(msg.Body);
        if (evt == null)
        {
            Acknowledge(msg.PayloadId, WebhookHandleStatus.Failed, "Unparseable network payload");
            return;
        }

        _log.Info("Network event '{Name}': {Message}", evt.Name, evt.Message);

        if (evt.Parameters.TryGetValue("mac", out var clientMac) && !string.IsNullOrEmpty(clientMac))
        {
            if (_configuredDevices.TryGetValue(clientMac, out var deviceId))
            {
                var isConnect = evt.Name.Contains("connect", StringComparison.OrdinalIgnoreCase)
                    && !evt.Name.Contains("disconnect", StringComparison.OrdinalIgnoreCase);
                var isDisconnect = evt.Name.Contains("disconnect", StringComparison.OrdinalIgnoreCase);

                if (isConnect)
                    _shardProxy.Tell(new DeviceStateUpdate(deviceId, "presence", true));
                else if (isDisconnect)
                    _shardProxy.Tell(new DeviceStateUpdate(deviceId, "presence", false));
            }
        }

        Acknowledge(msg.PayloadId, WebhookHandleStatus.Handled, null);
    }

    private void Acknowledge(Guid payloadId, WebhookHandleStatus status, string? error) =>
        _webhookRegistry.Tell(new WebhookHandled(payloadId, status, error, DateTimeOffset.UtcNow));
}
