using System.Text.Json;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using MQTTnet;
using MQTTnet.Formatter;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Roborock;

public sealed class RoborockDeviceActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer;
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _mediator;
    private readonly RoborockDeviceCredential _cred;
    private readonly Guid _deviceId;
    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _stateTopic;
    private readonly string _commandTopic;

    private IMqttClient? _mqttClient;
    private Channel<string>? _inboundChannel;
    private Channel<DeviceCommand>? _outboundChannel;
    private RoborockTransport _transport = RoborockTransport.Offline;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);

    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class ScheduleReconnect { public static readonly ScheduleReconnect Instance = new(); }

    public static Props Props(RoborockDeviceCredential cred, Guid deviceId,
        string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new RoborockDeviceActor(cred, deviceId, brokerHost, brokerPort, baseTopic));

    public RoborockDeviceActor(RoborockDeviceCredential cred, Guid deviceId,
        string brokerHost, int brokerPort, string baseTopic)
    {
        _cred = cred;
        _deviceId = deviceId;
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _stateTopic = $"{baseTopic}/{cred.Duid}";
        _commandTopic = $"{baseTopic}/{cred.Duid}/set";

        _materializer = Context.Materializer();
        _shardProxy = ActorRegistry.For(Context.System).Get<DeviceTwinRegion>();
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());
        Receive<ScheduleReconnect>(_ =>
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, ReconnectDelay));
        Receive<DeviceCommand>(cmd => _outboundChannel?.Writer.TryWrite(cmd));
    }

    protected override void PreStart() { base.PreStart(); Self.Tell(ConnectToBroker.Instance); }

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

            _mqttClient = new MqttClientFactory().CreateMqttClient();
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_brokerHost, _brokerPort)
                .WithProtocolVersion(MqttProtocolVersion.V311).Build();

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
            _mqttClient.DisconnectedAsync += _ => { self.Tell(ScheduleReconnect.Instance); return Task.CompletedTask; };

            var result = await _mqttClient.ConnectAsync(options, CancellationToken.None);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                _log.Warning("Roborock broker connect for {Duid}: {Code}", _cred.Duid, result.ResultCode);
                SetTransport(RoborockTransport.Offline);
                Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, ReconnectDelay);
                return;
            }

            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(_stateTopic).WithAtMostOnceQoS().Build(), CancellationToken.None);

            StartInboundStream();
            StartOutboundStream();
            _log.Info("Roborock {Duid} consuming {Topic}", _cred.Duid, _stateTopic);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect Roborock {Duid} to broker", _cred.Duid);
            SetTransport(RoborockTransport.Offline);
            Timers.StartSingleTimer("reconnect", ConnectToBroker.Instance, ReconnectDelay);
        }
    }

    private void StartInboundStream()
    {
        var deviceId = _deviceId;
        var shardProxy = _shardProxy;
        var materializer = _materializer;
        var log = _log;
        var duid = _cred.Duid;
        var self = Self;

        ChannelSource.FromReader<string>(_inboundChannel!.Reader)
            .Select(payload =>
            {
                self.Tell(new TransportFromPayload(payload));
                try { return RoborockStateMapper.MapState(payload); }
                catch (Exception ex)
                {
                    log.Warning(ex, "Failed to map Roborock state for {Duid}", duid);
                    return (IReadOnlyList<(string, object)>)Array.Empty<(string, object)>();
                }
            })
            .SelectMany(u => u)
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
            .Select(cmd => RoborockCommandBuilder.Build(cmd.CapabilityKey, cmd.Value))
            .Where(payload => payload != null)
            .SelectAsync(1, async payload =>
            {
                await mqttClient.PublishStringAsync(commandTopic, payload!, cancellationToken: CancellationToken.None);
                log.Info("Sent Roborock command to {Topic}: {Payload}", commandTopic, payload);
                return payload;
            })
            .To(Sink.Ignore<string?>())
            .Run(materializer);
    }

    private sealed record TransportFromPayload(string Payload);

    private void SetTransport(RoborockTransport transport)
    {
        if (_transport == transport) return;
        _transport = transport;
        var status = transport switch
        {
            RoborockTransport.Local => "local",
            RoborockTransport.Cloud => "cloud",
            _ => "offline",
        };
        _log.Info("Device {Duid} transport changed to {Status}", _cred.Duid, status);
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate($"roborock/{_cred.Duid}", status, 1)));
    }

    // Handle the transport hint extracted from each inbound payload.
    protected override bool AroundReceive(Receive receive, object message)
    {
        if (message is TransportFromPayload t)
        {
            try
            {
                using var doc = JsonDocument.Parse(t.Payload);
                if (doc.RootElement.TryGetProperty("_transport", out var tr))
                    SetTransport(tr.GetString() switch
                    {
                        "local" => RoborockTransport.Local,
                        "cloud" => RoborockTransport.Cloud,
                        _ => RoborockTransport.Offline,
                    });
            }
            catch { /* ignore malformed */ }
            return true;
        }
        return base.AroundReceive(receive, message);
    }
}
