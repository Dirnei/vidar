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

namespace Vidar.Communication.Loxone;

// One actor per Miniserver. Subscribes to loxone2mqtt/<serial>/# — the retained `structure`
// drives discovery (forwarded to the parent bridge), per-control `<uuid>` topics are state,
// and DeviceCommands are published to loxone2mqtt/<serial>/<uuid>/set. Structure re-sync is
// automatic: a republished structure re-runs discovery + emits a manifest snapshot for retire.
public sealed class LoxoneMiniserverActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer;
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _mediator;
    private readonly IActorRef _bridge;
    private readonly LoxoneMiniserver _ms;
    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _baseTopic;
    private readonly string _structureTopic;
    private readonly string _stateWildcard;

    // uuid -> (deviceId, controlType) learned from the latest structure, for state routing + commands.
    // DeviceId starts as Guid.Empty until the bridge's ControlIds reply arrives (the bridge owns
    // id assignment via GetDeviceId/Discover); state updates for a control are skipped until then.
    private readonly Dictionary<string, (Guid DeviceId, string Type)> _controls = new();

    // uuid -> latest raw state payload seen, cached regardless of whether the control's DeviceId is
    // known yet. Retained state can arrive before the structure and ControlIds that resolve the id
    // (and in any order between them), so we always keep the newest payload and (re)apply it the
    // moment the id resolves — otherwise initial retained state is lost to that ordering race.
    private readonly Dictionary<string, string> _latestState = new();

    private IMqttClient? _mqttClient;
    private Channel<MqttMessage>? _inbound;
    private Channel<DeviceCommand>? _outbound;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);

    private sealed record MqttMessage(string Topic, string Payload);
    private sealed class Connect { public static readonly Connect Instance = new(); }
    private sealed class ScheduleReconnect { public static readonly ScheduleReconnect Instance = new(); }

    public static Props Props(LoxoneMiniserver ms, string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new LoxoneMiniserverActor(ms, brokerHost, brokerPort, baseTopic));

    public LoxoneMiniserverActor(LoxoneMiniserver ms, string brokerHost, int brokerPort, string baseTopic)
    {
        _ms = ms;
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _baseTopic = baseTopic;
        _structureTopic = $"{baseTopic}/{ms.Serial}/structure";
        _stateWildcard = $"{baseTopic}/{ms.Serial}/#";

        _materializer = Context.Materializer();
        _shardProxy = ActorRegistry.For(Context.System).Get<DeviceTwinRegion>();
        _mediator = DistributedPubSub.Get(Context.System).Mediator;
        _bridge = Context.Parent;

        ReceiveAsync<Connect>(_ => ConnectAsync());

        Receive<ScheduleReconnect>(_ =>
            Timers.StartSingleTimer("reconnect", Connect.Instance, ReconnectDelay));

        // Parsed inbound MQTT messages are handled on the actor thread (mutating _controls must be
        // single-threaded). Receive handlers MUST be registered during construction, so it lives
        // here — the inbound stream (StartInboundStream) only forwards MqttMessage to Self.
        Receive<MqttMessage>(HandleInbound);

        // The bridge's reply to our ControlsDiscovered: uuid -> deviceId, computed via its own
        // GetDeviceId/Discover. Merge into _controls, preserving the control type we already know.
        Receive<ControlIds>(msg =>
        {
            foreach (var (uuid, deviceId) in msg.ByUuid)
                ResolveControl(uuid, deviceId);
        });

        // The bridge resolved (or corrected) a control's deviceId after a registration arrived —
        // re-tag it and flush its cached state (fixes the structure-vs-registration startup race).
        Receive<ResolveControlId>(msg => ResolveControl(msg.Uuid, msg.DeviceId));

        Receive<DeviceCommand>(cmd =>
        {
            var uuid = cmd.NativeId.Split('/', 2) is [_, var u] ? u : null;
            if (uuid is null) return;
            _outbound?.Writer.TryWrite(cmd);
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        Self.Tell(Connect.Instance);
    }

    protected override void PostStop()
    {
        _inbound?.Writer.TryComplete();
        _outbound?.Writer.TryComplete();
        try { _mqttClient?.DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        _mqttClient?.Dispose();
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate($"loxone/{_ms.Serial}", "offline", 0)));
        base.PostStop();
    }

    private async Task ConnectAsync()
    {
        try
        {
            _inbound?.Writer.TryComplete();
            _outbound?.Writer.TryComplete();
            if (_mqttClient != null) { try { await _mqttClient.DisconnectAsync(); } catch { } _mqttClient.Dispose(); }

            _mqttClient = new MqttClientFactory().CreateMqttClient();
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_brokerHost, _brokerPort)
                .WithProtocolVersion(MqttProtocolVersion.V311)
                .Build();

            _inbound = Channel.CreateBounded<MqttMessage>(2000);
            _outbound = Channel.CreateBounded<DeviceCommand>(200);
            var inbound = _inbound;
            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                inbound.Writer.TryWrite(new MqttMessage(e.ApplicationMessage.Topic,
                    e.ApplicationMessage.ConvertPayloadToString() ?? ""));
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
                Timers.StartSingleTimer("reconnect", Connect.Instance, ReconnectDelay);
                return;
            }

            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(_stateWildcard).WithAtMostOnceQoS().Build(), CancellationToken.None);

            StartInboundStream();
            StartOutboundStream();
            _mediator.Tell(new Publish("application-status",
                new ApplicationStatusUpdate($"loxone/{_ms.Serial}", "local", 0)));
            _log.Info("Loxone {Serial} consuming {Topic}", _ms.Serial, _stateWildcard);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Loxone {Serial} broker connect failed", _ms.Serial);
            Timers.StartSingleTimer("reconnect", Connect.Instance, ReconnectDelay);
        }
    }

    private void StartInboundStream()
    {
        var self = Self;
        ChannelSource.FromReader(_inbound!.Reader)
            .To(Sink.ForEach<MqttMessage>(m => self.Tell(m)))
            .Run(_materializer);
        // The Receive<MqttMessage> handler is registered in the constructor (handlers must be).
    }

    private void HandleInbound(MqttMessage m)
    {
        if (m.Topic == _structureTopic)
        {
            var structure = LoxoneStructureParser.Parse(m.Payload);
            if (structure is null) return;

            _controls.Clear();
            foreach (var c in structure.Controls)
            {
                if (LoxoneControlMapper.Map(c).Count == 0) continue;
                _controls[c.Uuid] = (Guid.Empty, c.Type);
            }

            // The bridge replies with ControlIds (uuid -> deviceId), handled above, once it has
            // run Discover() for each control and assigned/looked-up stable ids.
            _bridge.Tell(new ControlsDiscovered(_ms.Serial, structure));

            // Manifest snapshot for retire (Task 7): the complete set of nativeIds for this serial.
            var nativeIds = structure.Controls
                .Where(c => LoxoneControlMapper.Map(c).Count > 0)
                .Select(c => $"{_ms.Serial}/{c.Uuid}")
                .ToList();
            _mediator.Tell(new Publish("device-manifest",
                new DeviceManifestSnapshot("loxone", _ms.Serial, nativeIds)));
            return;
        }

        // State topic: loxone2mqtt/<serial>/<uuid>. A control uuid may itself contain a slash (a
        // LightControllerV2's split "<uuid>/<sub>" circuits), so we can't reject on '/'; ignore only
        // our own "<uuid>/set" command echoes and rely on the _controls membership check below
        // (structure/rooms and set-echoes are never keys in it).
        var prefix = $"{_baseTopic}/{_ms.Serial}/";
        if (!m.Topic.StartsWith(prefix)) return;
        var uuid = m.Topic[prefix.Length..];
        if (uuid.EndsWith("/set")) return; // ignore our own command echoes
        // Cache the newest payload unconditionally — even if this control (or its DeviceId) isn't
        // known yet — so it can be applied once ControlIds resolves the id, whatever the delivery
        // order of structure/state/ControlIds.
        _latestState[uuid] = m.Payload;
        if (!_controls.TryGetValue(uuid, out var ctrl)) return;
        if (ctrl.DeviceId == Guid.Empty) return; // applied when the id resolves
        ApplyState(ctrl.DeviceId, ctrl.Type, m.Payload);
    }

    // Record a control's resolved deviceId and, if it's a real id, apply its latest cached state.
    // Shared by the ControlIds reply (bulk, at discovery) and ResolveControlId (a later single
    // registration) so both close the structure-vs-registration ordering race the same way.
    private void ResolveControl(string uuid, Guid deviceId)
    {
        if (!_controls.TryGetValue(uuid, out var ctrl)) return;
        _controls[uuid] = (deviceId, ctrl.Type);
        if (deviceId != Guid.Empty && _latestState.TryGetValue(uuid, out var payload))
            ApplyState(deviceId, ctrl.Type, payload);
    }

    private void ApplyState(Guid deviceId, string controlType, string payload)
    {
        foreach (var (key, value) in LoxoneStateMapper.MapState(controlType, payload))
            _shardProxy.Tell(new DeviceStateUpdate(deviceId, key, value));
    }

    private void StartOutboundStream()
    {
        var mqttClient = _mqttClient!;
        var baseTopic = _baseTopic;
        var serial = _ms.Serial;
        var log = _log;
        ChannelSource.FromReader(_outbound!.Reader)
            .Select(cmd => (cmd.NativeId, Payload: LoxoneCommandBuilder.Build(cmd.CapabilityKey, cmd.Value)))
            .Where(x => x.Payload != null)
            .SelectAsync(1, async x =>
            {
                var uuid = x.NativeId.Split('/', 2)[1];
                var topic = $"{baseTopic}/{serial}/{uuid}/set";
                await mqttClient.PublishStringAsync(topic, x.Payload!, cancellationToken: CancellationToken.None);
                log.Info("Sent Loxone command {Topic}: {Payload}", topic, x.Payload);
                return x.Payload;
            })
            .To(Sink.Ignore<string?>())
            .Run(_materializer);
    }
}
