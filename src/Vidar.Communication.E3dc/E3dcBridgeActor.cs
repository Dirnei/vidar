using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using E3dc;
using E3dc.Client;
using E3dc.Descriptors;
using E3dc.Messages;
using E3dc.Messages.Responses;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.E3dc;

public sealed class E3dcBridgeActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _pluginRegistry;
    private readonly IActorRef _mediator;

    private RscpClient? _client;
    private IDisposable? _subscription;
    private Guid _deviceId;
    private bool _discovered;

    private static readonly TagDescriptor[] PollingTags =
    [
        Ems.PowerPv, Ems.PowerBat, Ems.PowerHome, Ems.PowerGrid, Ems.PowerAdd,
        Ems.BatSoc, Ems.Autarky, Ems.SelfConsumption
    ];

    public static Props Props(IActorRef shardProxy, IActorRef pluginRegistry) =>
        Akka.Actor.Props.Create(() => new E3dcBridgeActor(shardProxy, pluginRegistry));

    public E3dcBridgeActor(IActorRef shardProxy, IActorRef pluginRegistry)
    {
        _shardProxy = shardProxy;
        _pluginRegistry = pluginRegistry;
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<PluginRegistered>(OnPluginRegistered);
        Receive<IntegrationConfigChanged>(OnConfigChanged);
        Receive<RegisterDeviceForPolling>(OnRegisterDevice);
        Receive<SnapshotReceived>(OnSnapshot);
    }

    protected override void PreStart()
    {
        base.PreStart();
        _pluginRegistry.Tell(new RegisterPlugin("e3dc", Self));
    }

    protected override void PostStop()
    {
        DisposeClient();
        base.PostStop();
    }

    private void OnPluginRegistered(PluginRegistered msg)
    {
        foreach (var reg in msg.Registrations)
        {
            _deviceId = reg.DeviceId;
            _log.Info("Registered E3DC device {DeviceId}", reg.DeviceId);
        }

        if (msg.Enabled)
            StartClient(msg.Settings);
        else
            _log.Info("E3DC plugin registered but disabled");
    }

    private void OnConfigChanged(IntegrationConfigChanged msg)
    {
        if (msg.IntegrationId != "e3dc") return;

        if (msg.Enabled)
            StartClient(msg.Settings);
        else
        {
            DisposeClient();
            PublishStatus("stopped", 0);
        }
    }

    private void OnRegisterDevice(RegisterDeviceForPolling msg)
    {
        if (msg.CommunicationType != "e3dc") return;
        _deviceId = msg.DeviceId;
    }

    private void StartClient(Dictionary<string, string> settings)
    {
        DisposeClient();

        var host = settings.GetValueOrDefault("host", "");
        if (string.IsNullOrEmpty(host))
        {
            _log.Warning("E3DC host not configured — staying idle");
            PublishStatus("error", 0, "Host not configured");
            return;
        }

        var port = int.TryParse(settings.GetValueOrDefault("port"), out var p) ? p : 5033;
        var user = settings.GetValueOrDefault("user", "");
        var password = settings.GetValueOrDefault("password", "");
        var rscpKey = settings.GetValueOrDefault("rscpKey", "");
        var pollSeconds = int.TryParse(settings.GetValueOrDefault("pollingInterval"), out var pi) ? pi : 2;

        _client = new RscpClientBuilder()
            .Connect(host, port)
            .WithCredentials(user, password)
            .WithEncryptionKey(rscpKey)
            .WithPolling(TimeSpan.FromSeconds(pollSeconds), PollingTags)
            .Build(Context.System);

        var self = Self;
        _subscription = _client.Subscribe<RscpDataResponse>(response =>
        {
            var snapshot = response.ToEmsPowerSnapshot();
            if (snapshot != null)
                self.Tell(new SnapshotReceived(snapshot));
        });

        _log.Info("E3DC client started, polling {Host}:{Port} every {Interval}s", host, port, pollSeconds);
        PublishStatus("running", _deviceId != Guid.Empty ? 1 : 0);

        if (!_discovered)
            DiscoverDevice(settings);
    }

    private void OnSnapshot(SnapshotReceived msg)
    {
        if (_deviceId == Guid.Empty) return;

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, msg.Snapshot);
        foreach (var update in updates)
            _shardProxy.Tell(update);
    }

    private void DiscoverDevice(Dictionary<string, string> settings)
    {
        _discovered = true;
        var host = settings.GetValueOrDefault("host", "unknown");
        var deviceId = _deviceId != Guid.Empty ? _deviceId : Guid.NewGuid();

        var discovered = new DeviceDiscovered(
            deviceId,
            "e3dc",
            $"e3dc-{host}",
            [CapabilityType.SolarProduction, CapabilityType.GridPower,
             CapabilityType.Consumption, CapabilityType.Battery, CapabilityType.Extras],
            new Dictionary<string, string>
            {
                ["host"] = host,
                ["model"] = "S10 Pro",
                ["manufacturer"] = "E3/DC"
            });

        _mediator.Tell(new Publish("device-discovered", discovered));
    }

    private void DisposeClient()
    {
        _subscription?.Dispose();
        _subscription = null;
        _client?.DisposeAsync().GetAwaiter().GetResult();
        _client = null;
    }

    private void PublishStatus(string status, int deviceCount, string? error = null)
    {
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate("e3dc", status, deviceCount, error)));
    }

    private sealed record SnapshotReceived(global::E3dc.EmsPowerSnapshot Snapshot);
}
