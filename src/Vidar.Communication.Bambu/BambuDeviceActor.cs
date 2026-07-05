using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using System.Threading.Channels;
using MQTTnet;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Bambu;

// One actor per accepted Bambu printer. Holds the single TLS MQTT connection to the printer's
// local LAN broker (built-in on the printer itself, not a shared broker): publishes commands to
// "device/{serial}/request", consumes state reports from "device/{serial}/report", maps them onto
// the device twin, and manages on-demand camera snapshots (via a short-lived ffmpeg RTSP capture).
public sealed class BambuDeviceActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer;
    private readonly IActorRef? _shardProxy;
    private readonly IActorRef _mediator;
    private readonly BambuConfig _cfg;
    private readonly Guid _deviceId;
    private readonly string _reportTopic;
    private readonly string _requestTopic;

    private IMqttClient? _mqttClient;
    private Channel<string>? _inbound;
    private byte[]? _latestJpeg;
    private string? _lastState;
    private bool _online;

    // Fixed backoff for reconnecting to the printer's local broker (no cloud rate limits here).
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(15);

    private sealed class Connect { public static readonly Connect Instance = new(); }
    private sealed class Reconnect { public static readonly Reconnect Instance = new(); }
    private sealed record AutoSnapshot;
    private sealed record SnapshotCaptured(byte[]? Jpeg);
    private sealed record StateObserved(string State);

    public static Props Props(BambuConfig cfg, Guid deviceId) =>
        Akka.Actor.Props.Create(() => new BambuDeviceActor(cfg, deviceId));

    public BambuDeviceActor(BambuConfig cfg, Guid deviceId)
    {
        _cfg = cfg;
        _deviceId = deviceId;
        _reportTopic = $"device/{cfg.Serial}/report";
        _requestTopic = $"device/{cfg.Serial}/request";
        _materializer = Context.Materializer();

        var registry = ActorRegistry.For(Context.System);
        registry.TryGet<DeviceTwinRegion>(out _shardProxy); // guarded: unit tests construct without the shard proxy registered
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        ReceiveAsync<Connect>(_ => ConnectAsync());
        Receive<Reconnect>(_ =>
            Timers.StartSingleTimer("reconnect", Connect.Instance, ReconnectDelay));

        Receive<DeviceCommand>(cmd =>
        {
            if (cmd.CapabilityKey == "camera_snapshot")
            {
                Self.Tell(new AutoSnapshot());
                return;
            }
            var payload = BambuCommandBuilder.Build(cmd.CapabilityKey, cmd.Value);
            if (payload != null)
            {
                PublishRequest(payload).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        _log.Warning(t.Exception!.Flatten().InnerException, "Bambu {Serial} publish failed for {Key}", _cfg.Serial, cmd.CapabilityKey);
                }, TaskScheduler.Default);
            }
        });

        Receive<CaptureSnapshot>(_ =>
        {
            Sender.Tell(new SnapshotResult(_latestJpeg));
            Self.Tell(new AutoSnapshot()); // refresh the cache for the next ask
        });

        ReceiveAsync<AutoSnapshot>(async _ =>
        {
            var jpeg = await BambuSnapshot.CaptureAsync(_cfg.Host, _cfg.AccessCode, SnapshotTimeout, CancellationToken.None);
            Self.Tell(new SnapshotCaptured(jpeg));
        });

        Receive<SnapshotCaptured>(m =>
        {
            if (m.Jpeg != null) _latestJpeg = m.Jpeg;
        });

        // Folded in here (rather than added post-construction) because Akka.NET Receive<T>
        // handlers must be registered from inside the constructor. A state transition into a
        // terminal state (finish/fail/pause) triggers a fresh snapshot for the device history.
        Receive<StateObserved>(m =>
        {
            if (IsTerminal(m.State) && m.State != _lastState) Self.Tell(new AutoSnapshot());
            _lastState = m.State;
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
        try { _mqttClient?.DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        _mqttClient?.Dispose();
        base.PostStop();
    }

    private Task PublishRequest(string payload) =>
        _mqttClient?.PublishStringAsync(_requestTopic, payload) ?? Task.CompletedTask;

    private async Task ConnectAsync()
    {
        try
        {
            _inbound?.Writer.TryComplete();

            if (_mqttClient != null)
            {
                try { await _mqttClient.DisconnectAsync(); } catch { }
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            _inbound = Channel.CreateBounded<string>(1000);
            var inbound = _inbound;
            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var s = e.ApplicationMessage.ConvertPayloadToString();
                if (s != null) inbound.Writer.TryWrite(s);
                return Task.CompletedTask;
            };
            var self = Self;
            _mqttClient.DisconnectedAsync += _ =>
            {
                self.Tell(Reconnect.Instance);
                return Task.CompletedTask;
            };

            var result = await _mqttClient.ConnectAsync(BambuMqttOptions.Build(_cfg.Host, 8883, _cfg.AccessCode));
            if (result.ResultCode != MqttClientConnectResultCode.Success)
            {
                _log.Warning("Bambu {Serial} broker connect: {Code}", _cfg.Serial, result.ResultCode);
                SetOnline(false);
                Timers.StartSingleTimer("reconnect", Connect.Instance, ReconnectDelay);
                return;
            }

            await PublishRequest("""{"pushing":{"command":"pushall","sequence_id":"0"}}""");
            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_reportTopic).WithAtMostOnceQoS().Build());

            StartInboundStream();
            SetOnline(true);
            _log.Info("Bambu {Serial} connected to {Host}:8883", _cfg.Serial, _cfg.Host);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Bambu {Serial} connect failed", _cfg.Serial);
            SetOnline(false);
            Timers.StartSingleTimer("reconnect", Connect.Instance, ReconnectDelay);
        }
    }

    private void StartInboundStream()
    {
        var self = Self;
        var deviceId = _deviceId;
        var shard = _shardProxy;
        var log = _log;
        var serial = _cfg.Serial;

        ChannelSource.FromReader(_inbound!.Reader)
            .SelectMany(payload =>
            {
                try
                {
                    var updates = BambuStateMapper.Map(payload);
                    foreach (var (k, v) in updates)
                        if (k == "state" && v is string s) self.Tell(new StateObserved(s));
                    return updates;
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "Failed to map Bambu report payload for {Serial}", serial);
                    return (IReadOnlyList<(string CapabilityKey, object Value)>)Array.Empty<(string, object)>();
                }
            })
            .To(Sink.ForEach<(string CapabilityKey, object Value)>(u =>
                shard?.Tell(new DeviceStateUpdate(deviceId, u.CapabilityKey, u.Value))))
            .Run(_materializer);
    }

    private static bool IsTerminal(string s) => s is "FINISH" or "FAILED" or "PAUSE";

    private void SetOnline(bool online)
    {
        if (_online == online) return;
        _online = online;
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate($"bambu/{_cfg.Serial}", online ? "online" : "offline", 1)));
    }
}
