using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

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

    protected abstract string PluginId { get; }

    protected PluginActorBase(IActorRef pluginRegistry, IActorRef shardProxy)
    {
        PluginRegistry = pluginRegistry;
        ShardProxy = shardProxy;
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
        Mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate(PluginId, status, deviceCount, error)));
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
