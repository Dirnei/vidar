using Akka.Actor;
using Akka.Event;
using E3dc;
using E3dc.Client;
using E3dc.Descriptors;
using E3dc.Messages;
using E3dc.Messages.Responses;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.E3dc;

public sealed class E3dcBridgeActor : PluginActorBase
{
    private RscpClient? _client;
    private IDisposable? _subscription;
    private Guid _deviceId;
    private bool _discovered;
    private Dictionary<string, string> _settings = new();

    private sealed class StatusTick { public static readonly StatusTick Instance = new(); }

    private static readonly TagDescriptor[] PollingTags =
    [
        Ems.PowerPv, Ems.PowerBat, Ems.PowerHome, Ems.PowerGrid, Ems.PowerAdd,
        Ems.BatSoc, Ems.Autarky, Ems.SelfConsumption
    ];

    protected override string PluginId => "e3dc";

    public static Props Props(IActorRef pluginRegistry, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new E3dcBridgeActor(pluginRegistry, shardProxy));

    public E3dcBridgeActor(IActorRef pluginRegistry, IActorRef shardProxy)
        : base(pluginRegistry, shardProxy)
    {
        Receive<SnapshotReceived>(OnSnapshot);
        Receive<StatusTick>(_ => OnStatusTick());
    }

    protected override void PostStop()
    {
        DisposeClient();
        base.PostStop();
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        foreach (var reg in registrations)
        {
            _deviceId = reg.DeviceId;
            Log.Info("Registered E3DC device {DeviceId}", reg.DeviceId);
        }

        if (enabled)
            StartClient(settings);
        else
            Log.Info("E3DC plugin registered but disabled");
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (enabled)
            StartClient(settings);
        else
        {
            DisposeClient();
            PublishStatus("stopped", 0);
        }
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        _deviceId = deviceId;
    }

    private void StartClient(Dictionary<string, string> settings)
    {
        DisposeClient();
        _settings = settings;

        var host = settings.GetValueOrDefault("host", "");
        if (string.IsNullOrEmpty(host))
        {
            Log.Warning("E3DC host not configured — staying idle");
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

        Log.Info("E3DC client started, polling {Host}:{Port} every {Interval}s", host, port, pollSeconds);

        Timers.StartPeriodicTimer("status", StatusTick.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    private void OnStatusTick()
    {
        PublishStatus("running", _deviceId != Guid.Empty ? 1 : 0);

        if (!_discovered)
            DiscoverDevice(_settings);
    }

    private void OnSnapshot(SnapshotReceived msg)
    {
        if (_deviceId == Guid.Empty) return;

        var updates = E3dcStateMapper.MapSnapshot(_deviceId, msg.Snapshot);
        foreach (var update in updates)
            ReportState(update.DeviceId, update.CapabilityKey, update.Value);
    }

    private void DiscoverDevice(Dictionary<string, string> settings)
    {
        _discovered = true;
        var host = settings.GetValueOrDefault("host", "unknown");
        var deviceId = _deviceId != Guid.Empty ? _deviceId : Guid.NewGuid();

        var capabilities = new List<CapabilityDescriptor>
        {
            new() { Key = "solarProduction", Label = "Solar Production", Unit = UnitType.Watts },
            new() { Key = "gridPower", Label = "Grid Power", Unit = UnitType.Watts },
            new() { Key = "consumption", Label = "Consumption", Unit = UnitType.Watts },
            new() { Key = "batteryCharge", Label = "Battery Charge", Unit = UnitType.Percent, Min = 0, Max = 100 },
            new() { Key = "batteryPower", Label = "Battery Power", Unit = UnitType.Watts },
            new() { Key = "additionalPower", Label = "Additional Power", Unit = UnitType.Watts },
            new() { Key = "autarky", Label = "Autarky", Unit = UnitType.Percent, Min = 0, Max = 100 },
            new() { Key = "selfConsumption", Label = "Self Consumption", Unit = UnitType.Percent, Min = 0, Max = 100 },
        };

        Discover(deviceId, $"e3dc-{host}", capabilities, new Dictionary<string, string>
        {
            ["host"] = host,
            ["model"] = "S10 Pro",
            ["manufacturer"] = "E3/DC"
        });
    }

    private void DisposeClient()
    {
        Timers.Cancel("status");
        _subscription?.Dispose();
        _subscription = null;
        _client?.DisposeAsync().GetAwaiter().GetResult();
        _client = null;
    }

    private sealed record SnapshotReceived(global::E3dc.EmsPowerSnapshot Snapshot);
}
