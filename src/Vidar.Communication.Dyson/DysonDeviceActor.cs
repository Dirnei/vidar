using System.Security;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Dyson;

// One actor per accepted device. Mirrors Zigbee2MqttBridgeActor's HiveMQTT
// connect/subscribe/stream/reconnect wiring. Differences:
//   - broker host = cred.Ip, port 1883, username = cred.Serial, password = cred.MqttPassword
//   - subscribe   "<productType>/<serial>/status/#"
//   - on connect  publish REQUEST-CURRENT-STATE to "<productType>/<serial>/command"
//   - inbound     DysonStateMapper.MapState(payload, cred.ProductType) -> ReportState
//   - command     DysonCommandBuilder.Build(key, value, now) -> publish to command topic
//   - if cred.Ip is null/blank: do NOT connect; publish status needs-connection
//   - publish transport status via PublishStatus on every state change
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

    private HiveMQClient? _mqttClient;
    private Channel<MqttMessage>? _inboundChannel;
    private Channel<DeviceCommand>? _outboundChannel;
    private DysonTransport _transport = DysonTransport.NeedsConnection;

    private sealed record MqttMessage(string Topic, string Payload);
    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class CheckConnection { public static readonly CheckConnection Instance = new(); }

    public static Props Props(DysonDeviceCredential cred, Guid deviceId) =>
        Akka.Actor.Props.Create(() => new DysonDeviceActor(cred, deviceId));

    public DysonDeviceActor(DysonDeviceCredential cred, Guid deviceId)
    {
        _cred = cred;
        _deviceId = deviceId;
        _statusPrefix = $"{cred.ProductType}/{cred.Serial}/status/";
        _commandTopic = $"{cred.ProductType}/{cred.Serial}/command";

        _materializer = Context.Materializer();
        var actorRegistry = ActorRegistry.For(Context.System);
        _shardProxy = actorRegistry.Get<DeviceTwinRegion>();
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());

        Receive<CheckConnection>(_ =>
        {
            if (_mqttClient == null || !_mqttClient.IsConnected())
            {
                _log.Warning("MQTT client not connected for device {Serial}, reconnecting...", _cred.Serial);
                Self.Tell(ConnectToBroker.Instance);
            }
        });

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

        if (DysonDeviceState.Evaluate(_cred.Ip, connected: false) == DysonTransport.NeedsConnection)
        {
            SetTransport(DysonTransport.NeedsConnection);
            return;
        }

        Self.Tell(ConnectToBroker.Instance);
        Timers.StartPeriodicTimer("health", CheckConnection.Instance,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15));
    }

    protected override void PostStop()
    {
        _inboundChannel?.Writer.TryComplete();
        _outboundChannel?.Writer.TryComplete();
        _mqttClient?.DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        base.PostStop();
    }

    private async Task ConnectAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                try { await _mqttClient.DisconnectAsync(); } catch { }
                _mqttClient.Dispose();
                _mqttClient = null;
            }
            _inboundChannel?.Writer.TryComplete();
            _outboundChannel?.Writer.TryComplete();

            var secure = new SecureString();
            foreach (var c in _cred.MqttPassword) secure.AppendChar(c);
            secure.MakeReadOnly();

            var options = new HiveMQClientOptionsBuilder()
                .WithBroker(_cred.Ip!)
                .WithPort(1883)
                .WithClientId($"vidar-dyson-{_cred.Serial}-{Guid.NewGuid():N}")
                .WithUserName(_cred.Serial)
                .WithPassword(secure)
                .Build();

            _inboundChannel = Channel.CreateBounded<MqttMessage>(1000);
            _outboundChannel = Channel.CreateBounded<DeviceCommand>(100);

            _mqttClient = new HiveMQClient(options);

            var inbound = _inboundChannel;
            _mqttClient.OnMessageReceived += (_, e) =>
            {
                var topic = e.PublishMessage.Topic;
                var payload = e.PublishMessage.PayloadAsString;
                if (topic != null && payload != null)
                    inbound.Writer.TryWrite(new MqttMessage(topic, payload));
            };

            var self = Self;
            _mqttClient.OnDisconnectReceived += (_, _) =>
            {
                _log.Warning("MQTT disconnected for device {Serial}, scheduling reconnect...", _cred.Serial);
                self.Tell(ConnectToBroker.Instance);
            };

            var connectResult = await _mqttClient.ConnectAsync();
            _log.Info("MQTT connect result for {Serial}: {Result}", _cred.Serial, connectResult.ReasonCode);

            await _mqttClient.SubscribeAsync(
                $"{_cred.ProductType}/{_cred.Serial}/status/#",
                QualityOfService.AtLeastOnceDelivery);

            await _mqttClient.PublishAsync(
                _commandTopic,
                "{\"msg\":\"REQUEST-CURRENT-STATE\"}",
                QualityOfService.AtMostOnceDelivery);

            _log.Info("Connected to Dyson device {Serial} at {Ip}, subscribed to status/#",
                _cred.Serial, _cred.Ip);

            StartInboundStream();
            StartOutboundStream();

            SetTransport(DysonDeviceState.Evaluate(_cred.Ip, connected: true));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to Dyson device {Serial} at {Ip}", _cred.Serial, _cred.Ip);
            SetTransport(DysonTransport.Offline);
        }
    }

    private void StartInboundStream()
    {
        var prefix = _statusPrefix;
        var productType = _cred.ProductType;
        var deviceId = _deviceId;
        var shardProxy = _shardProxy;
        var materializer = _materializer;

        ChannelSource.FromReader<MqttMessage>(_inboundChannel!.Reader)
            .Where(msg => msg.Topic.StartsWith(prefix))
            .Select(msg => msg.Payload)
            .Select(payload =>
            {
                try { return DysonStateMapper.MapState(payload, productType); }
                catch { return (IReadOnlyList<(string, object)>)Array.Empty<(string, object)>(); }
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
                await mqttClient.PublishAsync(commandTopic, payload!, QualityOfService.AtMostOnceDelivery);
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
            DysonTransport.Offline => "offline",
            _ => "offline"
        };
        _log.Info("Device {Serial} transport changed to {Status}", _cred.Serial, status);
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate($"dyson/{_cred.Serial}", status, 1)));
    }
}
