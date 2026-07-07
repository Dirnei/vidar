using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Sharding;

namespace Vidar.Core.Plugins;

public abstract class PluginActorBase : ReceiveActor, IWithTimers
{
    protected ILoggingAdapter Log { get; } = Context.GetLogger();
    protected IActorRef ShardProxy { get; }
    protected IActorRef PluginRegistry { get; }
    protected IActorRef Mediator { get; }
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly Dictionary<string, Guid> _configuredDevices = new();
    private bool _enabled;
    private Dictionary<string, string> _settings = new();

    // Last status published; re-announced on a heartbeat (see PublishStatus).
    private string _lastStatus = "starting";
    private int _lastDeviceCount;
    private string? _lastError;

    private sealed class RepublishStatus { public static readonly RepublishStatus Instance = new(); }

    protected abstract string PluginId { get; }

    // A plugin declares its own kind. Providers expose devices; consumers act on them.
    // Consumer plugins override this; the host reads it off the status announcement and
    // never classifies plugins itself.
    protected virtual ApplicationType AppType => ApplicationType.Provider;

    protected PluginActorBase()
    {
        var actorRegistry = ActorRegistry.For(Context.System);
        PluginRegistry = actorRegistry.Get<PluginRegistry>();
        ShardProxy = actorRegistry.Get<DeviceTwinRegion>();
        Mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<ClusterEvent.CurrentClusterState>(_ => { });
        Receive<ClusterEvent.MemberUp>(msg =>
        {
            if (msg.Member.HasRole("host"))
                PluginRegistry.Tell(new RegisterPlugin(PluginId, Self));
        });

        Receive<PluginRegistered>(msg =>
        {
            foreach (var reg in msg.Registrations)
            {
                _configuredDevices[reg.NativeId] = reg.DeviceId;
                OnDeviceRegistered(reg.DeviceId, reg.NativeId, reg);
            }
            _enabled = msg.Enabled;
            _settings = msg.Settings;
            OnPluginRegistered(msg.Enabled, msg.Settings, msg.Registrations);
        });

        Receive<IntegrationConfigChanged>(msg =>
        {
            if (msg.IntegrationId != PluginId) return;
            _enabled = msg.Enabled;
            _settings = msg.Settings;
            OnConfigChanged(msg.Enabled, msg.Settings);
        });

        Receive<RegisterDeviceForPolling>(msg =>
        {
            if (msg.CommunicationType != PluginId) return;
            _configuredDevices[msg.NativeId] = msg.DeviceId;
            OnDeviceRegistered(msg.DeviceId, msg.NativeId, msg);
        });

        // Heartbeat: re-announce the last status so presence survives the application-status
        // pubsub subscription race on a freshly-started node and host/status-actor restarts.
        Receive<RepublishStatus>(_ => DoPublishStatus());
    }

    protected override void PreStart()
    {
        base.PreStart();
        Cluster.Get(Context.System).Subscribe(Self, typeof(ClusterEvent.MemberUp));
        PluginRegistry.Tell(new RegisterPlugin(PluginId, Self));
    }

    protected void Discover(Guid deviceId, string nativeId,
        List<CapabilityDescriptor> capabilities, Dictionary<string, string> metadata)
    {
        var msg = new DeviceDiscovered(deviceId, PluginId, nativeId, capabilities, metadata);
        Mediator.Tell(new Publish("device-discovered", msg));
    }

    protected void ReportState(Guid deviceId, string capabilityKey, object value)
    {
        ShardProxy.Tell(new DeviceStateUpdate(deviceId, capabilityKey, value));
    }

    protected void ReportOffline(Guid deviceId)
    {
        ShardProxy.Tell(new DeviceOffline(deviceId));
    }

    protected void PublishStatus(string status, int deviceCount, string? error = null)
    {
        _lastStatus = status;
        _lastDeviceCount = deviceCount;
        _lastError = error;
        DoPublishStatus();

        // The application-status pubsub is fire-and-forget: a fresh node's publish can arrive
        // before the host's subscription has gossiped to this node's mediator (silently lost as
        // dead letters), and the host holds status only in memory. Re-announce on a heartbeat so
        // the application reliably appears (and reappears after a host/status-actor restart).
        // Idempotent — the host just overwrites the latest status per application id.
        Timers.StartPeriodicTimer("status-heartbeat", RepublishStatus.Instance,
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15));
    }

    private void DoPublishStatus()
    {
        Mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate(PluginId, _lastStatus, _lastDeviceCount, _lastError, AppType)));
    }

    protected Guid? GetDeviceId(string nativeId) =>
        _configuredDevices.TryGetValue(nativeId, out var id) ? id : null;

    protected int ConfiguredDeviceCount => _configuredDevices.Count;
    protected bool IsEnabled => _enabled;
    protected IReadOnlyDictionary<string, string> Settings => _settings;

    protected virtual void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations) { }

    protected virtual void OnConfigChanged(bool enabled, Dictionary<string, string> settings) { }

    protected virtual void OnDeviceRegistered(Guid deviceId, string nativeId,
        RegisterDeviceForPolling registration) { }
}
