using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.UniFi;

public sealed class UniFiBridgeActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _shardProxy;

    // Current config — null until received from Host
    private UniFiApiClient? _client;
    private int _pollIntervalSeconds = 30;

    // State tracking
    private string _siteId = "";
    private readonly Dictionary<string, (Guid DeviceId, UniFiNetworkDevice Device)> _devicesByMac = new();
    private readonly Dictionary<string, (Guid ClientId, UniFiClient Client)> _clientsById = new();
    private readonly Dictionary<string, Guid> _configuredDevices = new();

    // Internal messages
    private sealed class PollTick { public static readonly PollTick Instance = new(); }
    private sealed class InitialFetch { public static readonly InitialFetch Instance = new(); }
    private sealed class RequestConfig { public static readonly RequestConfig Instance = new(); }

    public static Props Props(IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new UniFiBridgeActor(shardProxy));

    public UniFiBridgeActor(IActorRef shardProxy)
    {
        _shardProxy = shardProxy;

        // Handle config updates from pub/sub
        Receive<IntegrationConfigChanged>(msg =>
        {
            if (msg.IntegrationId != "unifi") return;
            _log.Info("Received IntegrationConfigChanged for unifi, enabled={Enabled}", msg.Enabled);
            ConfigureAndStart(msg.Enabled, msg.Settings);
        });

        ReceiveAsync<InitialFetch>(_ => InitialFetchAsync());
        ReceiveAsync<PollTick>(_ => PollAsync());

        Receive<RequestConfig>(_ =>
        {
            if (_client != null) return;
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Publish("request-integration-config", new RequestIntegrationConfig("unifi")));
            _log.Info("Requested integration config for unifi from Host");
            Timers.StartSingleTimer("config-retry", RequestConfig.Instance, TimeSpan.FromSeconds(5));
        });

        // Config cache: map nativeId → configured device GUID
        Receive<RegisterDeviceForPolling>(msg =>
        {
            if (msg.CommunicationType != "unifi") return;
            _configuredDevices[msg.NativeId] = msg.DeviceId;
        });

        Receive<RegistrationResponse>(msg =>
        {
            foreach (var reg in msg.Devices)
                _configuredDevices[reg.NativeId] = reg.DeviceId;
            _log.Info("Config cache loaded: {Count} UniFi devices", msg.Devices.Count);
        });

        // Handle port power-cycle commands from Pub/Sub
        Receive<DeviceCommand>(cmd =>
        {
            if (cmd.CommunicationType != "unifi") return;
            _ = HandleCommandAsync(cmd);
        });
    }

    protected override void PreStart()
    {
        base.PreStart();

        var mediator = DistributedPubSub.Get(Context.System).Mediator;

        // Subscribe to commands topic
        mediator.Tell(new Subscribe("commands.unifi", Self));

        // Subscribe to config updates and registrations
        mediator.Tell(new Subscribe("integration-config.unifi", Self));
        mediator.Tell(new Subscribe("register.unifi", Self));
        mediator.Tell(new Subscribe("registration-response.unifi", Self));

        // Request current config from Host after a short delay (let cluster form)
        Timers.StartSingleTimer("request-config", RequestConfig.Instance, TimeSpan.FromSeconds(5));
    }

    protected override void PostStop()
    {
        _client?.Dispose();
        base.PostStop();
    }

    private void ConfigureAndStart(bool enabled, Dictionary<string, string> settings)
    {
        // Stop any existing poll timer
        Timers.Cancel("poll");
        Timers.Cancel("initial-fetch");
        Timers.Cancel("initial-fetch-retry");

        // Dispose old client
        _client?.Dispose();
        _client = null;

        if (!enabled)
        {
            _log.Info("UniFi integration is disabled — polling stopped");
            PublishStatus("stopped");
            return;
        }

        if (!settings.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
        {
            _log.Warning("UniFi integration enabled but no host configured — waiting");
            return;
        }

        settings.TryGetValue("apiKey", out var apiKey);
        settings.TryGetValue("siteId", out var siteId);
        if (int.TryParse(settings.GetValueOrDefault("pollIntervalSeconds", "30"), out var interval) && interval > 0)
            _pollIntervalSeconds = interval;
        else
            _pollIntervalSeconds = 30;

        _client = new UniFiApiClient(host);
        _client.SetApiKey(apiKey ?? "");

        // Reset siteId so InitialFetch re-resolves it
        _siteId = siteId ?? "default";

        _log.Info("UniFi configured: host={Host}, siteId={SiteId}, pollInterval={Interval}s", host, _siteId, _pollIntervalSeconds);

        // Request device registrations from Host
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("request-registrations", new RequestRegistrations("unifi")));

        // Start initial fetch
        Timers.StartSingleTimer("initial-fetch", InitialFetch.Instance, TimeSpan.FromSeconds(2));
    }

    private async Task InitialFetchAsync()
    {
        if (_client == null)
        {
            _log.Warning("InitialFetch called without a configured client, skipping");
            return;
        }

        try
        {
            // Auto-detect site if not configured or use configured one
            if (string.IsNullOrEmpty(_siteId) || _siteId == "default")
            {
                var sites = await _client.GetSitesAsync();
                if (sites.Count == 1)
                {
                    _siteId = sites[0].Id;
                    _log.Info("Auto-detected UniFi site: {SiteId} ({Name})", _siteId, sites[0].Name ?? _siteId);
                }
                else if (sites.Count > 1)
                {
                    _siteId = sites[0].Id;
                    _log.Warning("Multiple UniFi sites found, using: {SiteId}. Set siteId in integration settings to configure.", _siteId);
                }
                else
                {
                    _log.Warning("No UniFi sites found, using default site ID");
                    _siteId = "default";
                }
            }

            _log.Info("Starting UniFi poll with site: {SiteId}", _siteId);
            await PollAsync();

            // Start periodic timer
            var interval = TimeSpan.FromSeconds(_pollIntervalSeconds);
            Timers.StartPeriodicTimer("poll", PollTick.Instance, interval, interval);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "UniFi initial fetch failed, retrying in 30s");
            PublishStatus("error", ex.Message);
            Timers.StartSingleTimer("initial-fetch-retry", InitialFetch.Instance, TimeSpan.FromSeconds(30));
        }
    }

    private async Task PollAsync()
    {
        if (_client == null)
        {
            _log.Warning("Poll called without configured client, skipping");
            return;
        }

        if (string.IsNullOrEmpty(_siteId))
        {
            _log.Warning("Poll called before site ID resolved, skipping");
            return;
        }

        try
        {
            await PollDevicesAsync();
            await PollClientsAsync();
            PublishStatus("running");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "UniFi poll failed");
            PublishStatus("error", ex.Message);
        }
    }

    private async Task PollDevicesAsync()
    {
        var devices = await _client!.GetDevicesAsync(_siteId);
        var mediator = DistributedPubSub.Get(Context.System).Mediator;

        foreach (var device in devices)
        {
            var mac = device.MacAddress;
            if (string.IsNullOrEmpty(mac)) continue;

            // Determine capabilities
            var capabilities = BuildDeviceCapabilities(device);

            // Build metadata
            var metadata = BuildDeviceMetadata(device);

            bool isNew = !_devicesByMac.ContainsKey(mac);

            if (isNew)
            {
                var id = Guid.NewGuid();
                _devicesByMac[mac] = (id, device);
                _log.Info("Discovered UniFi device: {Name} ({Mac})", device.Name ?? mac, mac);

                var discovered = new DeviceDiscovered(id, "unifi", mac, capabilities, metadata);
                mediator.Tell(new Publish("device-discovered", discovered));
            }
            else
            {
                var (existingId, _) = _devicesByMac[mac];
                _devicesByMac[mac] = (existingId, device);

                // Publish re-discovery to keep Host in sync
                var discovered = new DeviceDiscovered(existingId, "unifi", mac, capabilities, metadata);
                mediator.Tell(new Publish("device-discovered", discovered));
            }

            // Fetch stats only for configured devices
            if (_configuredDevices.TryGetValue(mac, out var configuredDeviceId))
            {
                var features = device.Features ?? [];
                if (features.Count > 0)
                    _ = FetchAndPublishDeviceStatsAsync(configuredDeviceId, device.Id);
            }
        }

        _log.Debug("UniFi poll: {Count} devices", devices.Count);
    }

    private async Task FetchAndPublishDeviceStatsAsync(Guid deviceId, string nativeDeviceId)
    {
        try
        {
            var stats = await _client!.GetDeviceStatsAsync(_siteId, nativeDeviceId);
            if (stats == null) return;

            // Publish Extras state as a dictionary
            var extras = new Dictionary<string, object>();
            if (stats.UptimeSec.HasValue) extras["uptime_sec"] = stats.UptimeSec.Value;
            if (stats.CpuUtilizationPct.HasValue) extras["cpu_pct"] = stats.CpuUtilizationPct.Value;
            if (stats.MemoryUtilizationPct.HasValue) extras["memory_pct"] = stats.MemoryUtilizationPct.Value;
            if (stats.LoadAverage1Min.HasValue) extras["load_1min"] = stats.LoadAverage1Min.Value;
            if (stats.Uplink != null)
            {
                if (stats.Uplink.TxRateBps.HasValue) extras["tx_bps"] = stats.Uplink.TxRateBps.Value;
                if (stats.Uplink.RxRateBps.HasValue) extras["rx_bps"] = stats.Uplink.RxRateBps.Value;
            }

            if (extras.Count > 0)
                _shardProxy.Tell(new DeviceStateUpdate(deviceId, CapabilityType.Extras, extras));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to fetch stats for device {DeviceId}", nativeDeviceId);
        }
    }

    private async Task PollClientsAsync()
    {
        var clients = await _client!.GetClientsAsync(_siteId);
        var mediator = DistributedPubSub.Get(Context.System).Mediator;

        // Track which clients are currently connected
        var connectedIds = new HashSet<string>(clients.Select(c => c.Id));

        foreach (var client in clients)
        {
            var clientId = client.Id;
            if (string.IsNullOrEmpty(clientId)) continue;

            var nativeId = string.IsNullOrEmpty(client.MacAddress) ? clientId : client.MacAddress;
            var capabilities = new List<CapabilityType> { CapabilityType.Presence };
            var metadata = BuildClientMetadata(client);

            bool isNew = !_clientsById.ContainsKey(clientId);

            if (isNew)
            {
                var id = Guid.NewGuid();
                _clientsById[clientId] = (id, client);
                _log.Info("Discovered UniFi client: {Name} ({Id})", client.Name ?? client.MacAddress ?? clientId, clientId);

                var discovered = new DeviceDiscovered(id, "unifi", nativeId, capabilities, metadata);
                mediator.Tell(new Publish("device-discovered", discovered));
            }
            else
            {
                var (existingId, _) = _clientsById[clientId];
                _clientsById[clientId] = (existingId, client);

                var discovered = new DeviceDiscovered(existingId, "unifi", nativeId, capabilities, metadata);
                mediator.Tell(new Publish("device-discovered", discovered));
            }

            // Send state update only if configured
            if (_configuredDevices.TryGetValue(nativeId, out var configuredId))
                _shardProxy.Tell(new DeviceStateUpdate(configuredId, CapabilityType.Presence, true));
        }

        // Mark previously-seen clients that are no longer connected as absent
        foreach (var (clientId, (_, client)) in _clientsById)
        {
            if (!connectedIds.Contains(clientId))
            {
                var cNativeId = string.IsNullOrEmpty(client.MacAddress) ? clientId : client.MacAddress;
                if (_configuredDevices.TryGetValue(cNativeId, out var cConfiguredId))
                    _shardProxy.Tell(new DeviceStateUpdate(cConfiguredId, CapabilityType.Presence, false));
            }
        }

        _log.Debug("UniFi poll: {Count} clients connected", clients.Count);
    }

    private async Task HandleCommandAsync(DeviceCommand cmd)
    {
        if (_client == null)
        {
            _log.Warning("Command received but UniFi is not configured yet");
            return;
        }

        try
        {
            // Find device by native ID (MAC)
            var entry = _devicesByMac.GetValueOrDefault(cmd.NativeId);
            if (entry == default)
            {
                _log.Warning("Unknown device for command: {NativeId}", cmd.NativeId);
                return;
            }

            if (cmd.Capability == CapabilityType.Power)
            {
                // Value should be port index for power cycle
                if (cmd.Value is int portIdx || (cmd.Value is long pl && (portIdx = (int)pl) >= 0))
                {
                    var success = await _client.PowerCyclePortAsync(_siteId, entry.Device.Id, portIdx);
                    _log.Info("Power cycle port {Port} on {Device}: {Result}",
                        portIdx, cmd.NativeId, success ? "success" : "failed");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to handle command for device {NativeId}", cmd.NativeId);
        }
    }

    private static List<CapabilityType> BuildDeviceCapabilities(UniFiNetworkDevice device)
    {
        var caps = new List<CapabilityType>();
        var features = device.Features ?? [];

        // Switching devices may have PoE ports — represent as Power capability
        if (features.Contains("switching"))
            caps.Add(CapabilityType.Power);

        // Always add Extras for network device stats
        caps.Add(CapabilityType.Extras);

        return caps;
    }

    private static Dictionary<string, string> BuildDeviceMetadata(UniFiNetworkDevice device)
    {
        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(device.Name)) metadata["name"] = device.Name;
        if (!string.IsNullOrEmpty(device.Model)) metadata["model"] = device.Model;
        if (!string.IsNullOrEmpty(device.State)) metadata["state"] = device.State;
        if (!string.IsNullOrEmpty(device.IpAddress)) metadata["ip"] = device.IpAddress;
        if (!string.IsNullOrEmpty(device.FirmwareVersion)) metadata["firmware"] = device.FirmwareVersion;
        if (device.Features?.Count > 0) metadata["features"] = string.Join(",", device.Features);
        return metadata;
    }

    private void PublishStatus(string status, string? error = null)
    {
        var deviceCount = _devicesByMac.Count + _clientsById.Count;
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate("unifi", status, deviceCount, error)));
    }

    private static Dictionary<string, string> BuildClientMetadata(UniFiClient client)
    {
        var metadata = new Dictionary<string, string>();
        var displayName = client.Name ?? client.MacAddress ?? client.Id;
        if (!string.IsNullOrEmpty(displayName)) metadata["name"] = displayName;
        if (!string.IsNullOrEmpty(client.MacAddress)) metadata["mac"] = client.MacAddress;
        if (!string.IsNullOrEmpty(client.IpAddress)) metadata["ip"] = client.IpAddress;
        if (!string.IsNullOrEmpty(client.Type)) metadata["type"] = client.Type;
        metadata["client_type"] = "wifi";
        return metadata;
    }
}
