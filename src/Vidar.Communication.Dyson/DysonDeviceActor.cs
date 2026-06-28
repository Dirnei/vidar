using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Formatter;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Dyson;

// One actor per accepted Dyson device. Newer Dyson devices only speak to the cloud (AWS IoT),
// which the standalone `dyson2mqtt` sidecar bridges onto the local MQTT broker. This actor
// consumes that: device state arrives on "<base>/<serial>"; commands are published to
// "<base>/<serial>/set" for the sidecar to forward to the device.
public sealed class DysonDeviceActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer;
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _mediator;
    private readonly DysonDeviceCredential _cred;
    private readonly Guid _deviceId;
    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _stateTopic;
    private readonly string _commandTopic;

    private IMqttClient? _mqttClient;
    private Channel<string>? _inboundChannel;
    private Channel<DeviceCommand>? _outboundChannel;
    private DysonTransport? _transport;

    // Fixed backoff for reconnecting to the local broker (no cloud rate limits to honour here).
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);

    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class ScheduleReconnect { public static readonly ScheduleReconnect Instance = new(); }

    public static Props Props(DysonDeviceCredential cred, Guid deviceId, string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new DysonDeviceActor(cred, deviceId, brokerHost, brokerPort, baseTopic));

    public DysonDeviceActor(DysonDeviceCredential cred, Guid deviceId, string brokerHost, int brokerPort, string baseTopic)
    {
        _cred = cred;
        _deviceId = deviceId;
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _stateTopic = $"{baseTopic}/{cred.Serial}";
        _commandTopic = $"{baseTopic}/{cred.Serial}/set";

        _materializer = Context.Materializer();
        var actorRegistry = ActorRegistry.For(Context.System);
        _shardProxy = actorRegistry.Get<DeviceTwinRegion>();
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());

        Receive<ScheduleReconnect>(_ =>
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, ReconnectDelay));

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

            if (_mqttClient != null)
            {
                try { await _mqttClient.DisconnectAsync(); } catch { }
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_brokerHost, _brokerPort)
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .Build();

            _inboundChannel = Channel.CreateBounded<string>(1000);
            _outboundChannel = Channel.CreateBounded<DeviceCommand>(100);
            var inbound = _inboundChannel;
            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var payload = e.ApplicationMessage.ConvertPayloadToString();
                if (payload != null) inbound.Writer.TryWrite(payload);
                return Task.CompletedTask;
            };
            var self = Self;
            _mqttClient.DisconnectedAsync += _ =>
            {
                self.Tell(ScheduleReconnect.Instance);
                return Task.CompletedTask;
            };

            var result = await _mqttClient.ConnectAsync(options, CancellationToken.None);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                _log.Warning("Dyson broker connect for {Serial}: {Code}", _cred.Serial, result.ResultCode);
                SetTransport(DysonTransport.Offline);
                Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, ReconnectDelay);
                return;
            }

            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(_stateTopic).WithAtMostOnceQoS().Build(), CancellationToken.None);

            StartInboundStream();
            StartOutboundStream();
            SetTransport(DysonTransport.Local); // "online"
            _log.Info("Dyson {Serial} consuming {Topic} from broker {Host}:{Port}",
                _cred.Serial, _stateTopic, _brokerHost, _brokerPort);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Dyson {Serial} to broker", _cred.Serial);
            SetTransport(DysonTransport.Offline);
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, ReconnectDelay);
        }
    }

    private void StartInboundStream()
    {
        var productType = _cred.ProductType;
        var deviceId = _deviceId;
        var shardProxy = _shardProxy;
        var materializer = _materializer;
        var log = _log;
        var serial = _cred.Serial;

        ChannelSource.FromReader<string>(_inboundChannel!.Reader)
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
