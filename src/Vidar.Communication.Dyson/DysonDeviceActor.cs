using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Dyson;

// One actor per accepted Dyson device. Connects to Dyson's AWS IoT cloud endpoint
// via MQTTnet over WebSocket (WSS). Credentials are fetched per-connect from DysonCloudIot.
// Reconnect is gated by DysonReconnectPolicy: 401 → NeedsReauth (no auto-retry),
// 429 → RateLimitedDelay (≥60s), other transient → TransientDelay (15s).
public sealed class DysonDeviceActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer;
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _mediator;
    private readonly DysonDeviceCredential _cred;
    private readonly Guid _deviceId;
    private readonly string _statusPrefix;
    private readonly string _commandTopic;
    private readonly string? _accountToken;
    private readonly DysonCloudIot _cloudIot;

    private IMqttClient? _mqttClient;
    private Channel<MqttMessage>? _inboundChannel;
    private Channel<DeviceCommand>? _outboundChannel;
    private DysonTransport? _transport;

    private sealed record MqttMessage(string Topic, string Payload);
    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class ScheduleReconnect { public static readonly ScheduleReconnect Instance = new(); }

    public static Props Props(DysonDeviceCredential cred, Guid deviceId, string? accountToken, DysonCloudIot cloudIot) =>
        Akka.Actor.Props.Create(() => new DysonDeviceActor(cred, deviceId, accountToken, cloudIot));

    public DysonDeviceActor(DysonDeviceCredential cred, Guid deviceId, string? accountToken, DysonCloudIot cloudIot)
    {
        _cred = cred;
        _deviceId = deviceId;
        _accountToken = accountToken;
        _cloudIot = cloudIot;
        _statusPrefix = $"{cred.ProductType}/{cred.Serial}/status/";
        _commandTopic = $"{cred.ProductType}/{cred.Serial}/command";

        _materializer = Context.Materializer();
        var actorRegistry = ActorRegistry.For(Context.System);
        _shardProxy = actorRegistry.Get<DeviceTwinRegion>();
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());

        Receive<ScheduleReconnect>(_ =>
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, DysonReconnectPolicy.TransientDelay));

        Receive<DeviceCommand>(cmd =>
        {
            if (_transport != DysonTransport.Local)
            {
                _log.Debug("Command ignored for {Serial}: transport is {Transport}", _cred.Serial, _transport);
                return;
            }
            _outboundChannel?.Writer.TryWrite(cmd);
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        Self.Tell(ConnectToBroker.Instance);
    }

    protected override void PostStop()
    {
        _inboundChannel?.Writer.TryComplete();
        _outboundChannel?.Writer.TryComplete();
        try { _mqttClient?.DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        _mqttClient?.Dispose();
        base.PostStop();
    }

    private async Task ConnectAsync()
    {
        try
        {
            _inboundChannel?.Writer.TryComplete();
            _outboundChannel?.Writer.TryComplete();

            if (string.IsNullOrWhiteSpace(_accountToken))
            {
                _log.Warning("No Dyson account token for {Serial}; needs re-auth", _cred.Serial);
                SetTransport(DysonTransport.NeedsReauth);
                return; // no auto-retry (rate-limit safety)
            }

            DysonIoTCredentials creds;
            try
            {
                creds = await _cloudIot.GetCredentialsAsync(_cred.Serial, _accountToken, CancellationToken.None);
            }
            catch (DysonAuthExpiredException)
            {
                _log.Warning("Dyson account token expired for {Serial}; needs re-auth", _cred.Serial);
                SetTransport(DysonTransport.NeedsReauth);
                return; // DysonReconnectPolicy: AuthExpired => no auto-retry
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to fetch IoT credentials for {Serial}", _cred.Serial);
                SetTransport(DysonTransport.Offline);
                ScheduleReconnectAfterFailure(ex);
                return;
            }

            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            var headers = new Dictionary<string, string>
            {
                [creds.TokenKey] = creds.TokenValue,
                ["X-Amz-CustomAuthorizer-Name"] = creds.CustomAuthorizerName,
                ["X-Amz-CustomAuthorizer-Signature"] = creds.TokenSignature,
            };

            var options = new MqttClientOptionsBuilder()
                .WithWebSocketServer(o => o.WithUri($"wss://{creds.Endpoint}/mqtt").WithRequestHeaders(headers))
                .WithClientId(creds.ClientId)
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .WithTlsOptions(o => o.UseTls(true))
                .Build();

            _inboundChannel = Channel.CreateBounded<MqttMessage>(1000);
            _outboundChannel = Channel.CreateBounded<DeviceCommand>(100);
            var inbound = _inboundChannel;
            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var topic = e.ApplicationMessage.Topic;
                var payload = e.ApplicationMessage.ConvertPayloadToString();
                if (topic != null && payload != null) inbound.Writer.TryWrite(new MqttMessage(topic, payload));
                return Task.CompletedTask;
            };
            var self = Self;
            _mqttClient.DisconnectedAsync += _ => { self.Tell(ScheduleReconnect.Instance); return Task.CompletedTask; };

            var result = await _mqttClient.ConnectAsync(options, CancellationToken.None);
            _log.Info("Dyson cloud connect for {Serial}: {Code}", _cred.Serial, result.ResultCode);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                SetTransport(DysonTransport.Offline);
                Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, DysonReconnectPolicy.TransientDelay);
                return;
            }

            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic($"{_cred.ProductType}/{_cred.Serial}/status/#").WithAtLeastOnceQoS().Build(), CancellationToken.None);
            await _mqttClient.PublishStringAsync(_commandTopic, "{\"msg\":\"REQUEST-CURRENT-STATE\"}", cancellationToken: CancellationToken.None);

            StartInboundStream();
            StartOutboundStream();
            SetTransport(DysonTransport.Local); // "online"
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to cloud-connect Dyson {Serial}", _cred.Serial);
            SetTransport(DysonTransport.Offline);
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, DysonReconnectPolicy.TransientDelay);
        }
    }

    // Inspect the exception for HTTP 429 → RateLimitedDelay; otherwise TransientDelay.
    private void ScheduleReconnectAfterFailure(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } httpEx)
        {
            var decision = DysonReconnectPolicy.Next(DysonConnectOutcome.RateLimited);
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, decision.Delay);
            _log.Warning("Dyson IoT-credentials rate-limited for {Serial}; retry in {Delay}", _cred.Serial, decision.Delay);
        }
        else
        {
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, DysonReconnectPolicy.TransientDelay);
        }
    }

    private void StartInboundStream()
    {
        var prefix = _statusPrefix;
        var productType = _cred.ProductType;
        var deviceId = _deviceId;
        var shardProxy = _shardProxy;
        var materializer = _materializer;
        var log = _log;
        var serial = _cred.Serial;

        ChannelSource.FromReader<MqttMessage>(_inboundChannel!.Reader)
            .Where(msg => msg.Topic.StartsWith(prefix))
            .Select(msg => msg.Payload)
            .Select(payload =>
            {
                try { return DysonStateMapper.MapState(payload, productType); }
                catch (Exception ex)
                {
                    log.Warning(ex, "Failed to map Dyson state payload for {Serial}", serial);
                    return (IReadOnlyList<(string, object)>)Array.Empty<(string, object)>();
                }
            })
            .SelectMany(updates => updates)
            .To(Sink.ForEach<(string CapabilityKey, object Value)>(u =>
                shardProxy.Tell(new DeviceStateUpdate(deviceId, u.CapabilityKey, u.Value))))
            .Run(materializer);
    }

    private void StartOutboundStream()
    {
        var mqttClient = _mqttClient!;
        var commandTopic = _commandTopic;
        var materializer = _materializer;
        var log = _log;

        ChannelSource.FromReader<DeviceCommand>(_outboundChannel!.Reader)
            .Select(cmd => DysonCommandBuilder.Build(cmd.CapabilityKey, cmd.Value, DateTimeOffset.UtcNow))
            .Where(payload => payload != null)
            .SelectAsync(1, async payload =>
            {
                await mqttClient.PublishStringAsync(commandTopic, payload!,
                    cancellationToken: CancellationToken.None);
                log.Info("Sent Dyson command to {Topic}: {Payload}", commandTopic, payload);
                return payload;
            })
            .To(Sink.Ignore<string?>())
            .Run(materializer);
    }

    private void SetTransport(DysonTransport transport)
    {
        if (_transport == transport) return;
        _transport = transport;
        var status = transport switch
        {
            DysonTransport.Local => "local",
            DysonTransport.NeedsConnection => "needs-connection",
            DysonTransport.NeedsReauth => "needs-reauth",
            DysonTransport.Offline => "offline",
            _ => "offline"
        };
        _log.Info("Device {Serial} transport changed to {Status}", _cred.Serial, status);
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate($"dyson/{_cred.Serial}", status, 1)));
    }
}
