using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Webhooks;
using Vidar.Communication.UniFi.Webhooks;

namespace Vidar.Communication.UniFi;

public sealed class UniFiBridgeActor : PluginActorBase
{
    private readonly IActorRef _webhookRegistry;
    private readonly string _hostUrl;
    private IActorRef _protectHandler = ActorRefs.Nobody;
    private IActorRef _networkHandler = ActorRefs.Nobody;

    // Current config — null until received from Host
    private UniFiApiClient? _client;
    private int _pollIntervalSeconds = 30;

    // State tracking
    private string _siteId = "";
    private readonly Dictionary<string, (Guid DeviceId, UniFiNetworkDevice Device)> _devicesByMac = new();
    private readonly Dictionary<string, (Guid ClientId, UniFiClient Client)> _clientsById = new();
    private readonly Dictionary<string, Guid> _configuredDevices = new();

    private UniFiProtectApiClient? _protectClient;
    private readonly Dictionary<string, (Guid DeviceId, UniFiCamera Camera)> _camerasByMac = new();

    protected override string PluginId => "unifi";

    // Internal messages
    private sealed class PollTick { public static readonly PollTick Instance = new(); }
    private sealed class InitialFetch { public static readonly InitialFetch Instance = new(); }
    private sealed class RegisterWebhooks { public static readonly RegisterWebhooks Instance = new(); }

    public static Props Props(string hostUrl) =>
        Akka.Actor.Props.Create(() => new UniFiBridgeActor(hostUrl));

    public UniFiBridgeActor(string hostUrl)
    {
        _webhookRegistry = ActorRegistry.For(Context.System).Get<WebhookRegistry>();
        _hostUrl = hostUrl.TrimEnd('/');

        ReceiveAsync<InitialFetch>(_ => InitialFetchAsync());
        ReceiveAsync<PollTick>(_ => PollAsync());

        // Handle port power-cycle commands
        Receive<DeviceCommand>(cmd =>
        {
            if (cmd.CommunicationType != "unifi") return;
            _ = HandleCommandAsync(cmd);
        });

        Receive<RegisterWebhooks>(_ => RegisterWebhookRoutes());
        Receive<WebhookRegistryStarted>(_ => RegisterWebhookRoutes());

        Receive<WebhookReceived>(msg =>
        {
            switch (msg.RouteKey)
            {
                case "unifi-protect": _protectHandler.Tell(msg); break;
                case "unifi-network": _networkHandler.Tell(msg); break;
                default:
                    _webhookRegistry.Tell(new WebhookHandled(
                        msg.PayloadId, WebhookHandleStatus.Failed,
                        $"Unknown route: {msg.RouteKey}", DateTimeOffset.UtcNow));
                    break;
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();

        _protectHandler = Context.ActorOf(
            ProtectAlarmWebhookHandlerActor.Props(_hostUrl, _configuredDevices),
            "protect-webhook");
        _networkHandler = Context.ActorOf(
            NetworkWebhookHandlerActor.Props(_hostUrl, _configuredDevices),
            "network-webhook");

        Mediator.Tell(new Subscribe("webhook-registry-started", Self));
        Timers.StartSingleTimer("webhook-register", RegisterWebhooks.Instance, TimeSpan.Zero);
    }

    protected override void PostStop()
    {
        _client?.Dispose();
        _protectClient?.Dispose();
        base.PostStop();
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        foreach (var reg in registrations)
            _configuredDevices[reg.NativeId] = reg.DeviceId;

        Log.Info("Plugin registered with {Count} devices, enabled={Enabled}", registrations.Count, enabled);

        if (enabled)
            ConfigureAndStart(true, settings);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        Log.Info("Received IntegrationConfigChanged for unifi, enabled={Enabled}", enabled);
        ConfigureAndStart(enabled, settings);
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        _configuredDevices[nativeId] = deviceId;
    }

    private void RegisterWebhookRoutes()
    {
        _webhookRegistry.Tell(new RegisterWebhookListener("unifi-protect", Self, IntegrationId: "unifi"));
        _webhookRegistry.Tell(new RegisterWebhookListener("unifi-network", Self, IntegrationId: "unifi"));
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
        _protectClient?.Dispose();
        _protectClient = null;

        if (!enabled)
        {
            Log.Info("UniFi integration is disabled — polling stopped");
            var deviceCount = _devicesByMac.Count + _clientsById.Count + _camerasByMac.Count;
            PublishStatus("stopped", deviceCount);
            return;
        }

        if (!settings.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
        {
            Log.Warning("UniFi integration enabled but no host configured — waiting");
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

        _protectClient?.Dispose();
        _protectClient = new UniFiProtectApiClient(host);
        _protectClient.SetApiKey(apiKey ?? "");

        // Reset siteId so InitialFetch re-resolves it
        _siteId = siteId ?? "default";

        Log.Info("UniFi configured: host={Host}, siteId={SiteId}, pollInterval={Interval}s", host, _siteId, _pollIntervalSeconds);

        // Start initial fetch
        Timers.StartSingleTimer("initial-fetch", InitialFetch.Instance, TimeSpan.FromSeconds(2));
    }

    private async Task InitialFetchAsync()
    {
        if (_client == null)
        {
            Log.Warning("InitialFetch called without a configured client, skipping");
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
                    Log.Info("Auto-detected UniFi site: {SiteId} ({Name})", _siteId, sites[0].Name ?? _siteId);
                }
                else if (sites.Count > 1)
                {
                    _siteId = sites[0].Id;
                    Log.Warning("Multiple UniFi sites found, using: {SiteId}. Set siteId in integration settings to configure.", _siteId);
                }
                else
                {
                    Log.Warning("No UniFi sites found, using default site ID");
                    _siteId = "default";
                }
            }

            Log.Info("Starting UniFi poll with site: {SiteId}", _siteId);
            await PollAsync();

            // Start periodic timer
            var interval = TimeSpan.FromSeconds(_pollIntervalSeconds);
            Timers.StartPeriodicTimer("poll", PollTick.Instance, interval, interval);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UniFi initial fetch failed, retrying in 30s");
            var deviceCount = _devicesByMac.Count + _clientsById.Count + _camerasByMac.Count;
            PublishStatus("error", deviceCount, ex.Message);
            Timers.StartSingleTimer("initial-fetch-retry", InitialFetch.Instance, TimeSpan.FromSeconds(30));
        }
    }

    private async Task PollAsync()
    {
        if (_client == null)
        {
            Log.Warning("Poll called without configured client, skipping");
            return;
        }

        if (string.IsNullOrEmpty(_siteId))
        {
            Log.Warning("Poll called before site ID resolved, skipping");
            return;
        }

        try
        {
            await PollDevicesAsync();
            await PollClientsAsync();
            await PollCamerasAsync();
            var deviceCount = _devicesByMac.Count + _clientsById.Count + _camerasByMac.Count;
            PublishStatus("running", deviceCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UniFi poll failed");
            var deviceCount = _devicesByMac.Count + _clientsById.Count + _camerasByMac.Count;
            PublishStatus("error", deviceCount, ex.Message);
        }
    }

    private async Task PollDevicesAsync()
    {
        var devices = await _client!.GetDevicesAsync(_siteId);

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
                Log.Info("Discovered UniFi device: {Name} ({Mac})", device.Name ?? mac, mac);

                Discover(id, mac, capabilities, metadata);
            }
            else
            {
                var (existingId, _) = _devicesByMac[mac];
                _devicesByMac[mac] = (existingId, device);

                // Publish re-discovery to keep Host in sync
                Discover(existingId, mac, capabilities, metadata);
            }

            // Send state for configured devices
            if (_configuredDevices.TryGetValue(mac, out var configuredDeviceId))
            {
                var isOnline = string.Equals(device.State, "ONLINE", StringComparison.OrdinalIgnoreCase);
                if (isOnline)
                {
                    _ = FetchAndPublishDeviceStatsAsync(configuredDeviceId, device.Id);
                }
                else
                {
                    ReportOffline(configuredDeviceId);
                }
            }
        }

        Log.Debug("UniFi poll: {Count} devices", devices.Count);
    }

    private async Task FetchAndPublishDeviceStatsAsync(Guid deviceId, string nativeDeviceId)
    {
        try
        {
            var stats = await _client!.GetDeviceStatsAsync(_siteId, nativeDeviceId);
            if (stats != null)
            {
                if (stats.UptimeSec.HasValue) ShardProxy.Tell(new DeviceStateUpdate(deviceId, "uptime", (double)stats.UptimeSec.Value));
                if (stats.CpuUtilizationPct.HasValue) ShardProxy.Tell(new DeviceStateUpdate(deviceId, "cpuUtilization", stats.CpuUtilizationPct.Value));
                if (stats.MemoryUtilizationPct.HasValue) ShardProxy.Tell(new DeviceStateUpdate(deviceId, "memoryUtilization", stats.MemoryUtilizationPct.Value));
                if (stats.LoadAverage1Min.HasValue) ShardProxy.Tell(new DeviceStateUpdate(deviceId, "loadAverage", stats.LoadAverage1Min.Value));
                if (stats.Uplink != null)
                {
                    if (stats.Uplink.TxRateBps.HasValue) ShardProxy.Tell(new DeviceStateUpdate(deviceId, "txRate", (double)stats.Uplink.TxRateBps.Value));
                    if (stats.Uplink.RxRateBps.HasValue) ShardProxy.Tell(new DeviceStateUpdate(deviceId, "rxRate", (double)stats.Uplink.RxRateBps.Value));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch stats for device {DeviceId}", nativeDeviceId);
        }
    }

    private async Task PollClientsAsync()
    {
        var clients = await _client!.GetClientsAsync(_siteId);

        // Track which clients are currently connected
        var connectedIds = new HashSet<string>(clients.Select(c => c.Id));

        foreach (var client in clients)
        {
            var clientId = client.Id;
            if (string.IsNullOrEmpty(clientId)) continue;

            var nativeId = string.IsNullOrEmpty(client.MacAddress) ? clientId : client.MacAddress;
            var capabilities = new List<CapabilityDescriptor>
            {
                new() { Key = "presence", Label = "Presence", Unit = UnitType.Detected }
            };
            var metadata = BuildClientMetadata(client);

            bool isNew = !_clientsById.ContainsKey(clientId);

            if (isNew)
            {
                var id = Guid.NewGuid();
                _clientsById[clientId] = (id, client);
                Log.Info("Discovered UniFi client: {Name} ({Id})", client.Name ?? client.MacAddress ?? clientId, clientId);

                Discover(id, nativeId, capabilities, metadata);
            }
            else
            {
                var (existingId, _) = _clientsById[clientId];
                _clientsById[clientId] = (existingId, client);

                Discover(existingId, nativeId, capabilities, metadata);
            }

            // Send state update only if configured
            if (_configuredDevices.TryGetValue(nativeId, out var configuredId))
                ShardProxy.Tell(new DeviceStateUpdate(configuredId, "presence", true));
        }

        // Mark previously-seen clients that are no longer connected as absent
        foreach (var (clientId, (_, client)) in _clientsById)
        {
            if (!connectedIds.Contains(clientId))
            {
                var cNativeId = string.IsNullOrEmpty(client.MacAddress) ? clientId : client.MacAddress;
                if (_configuredDevices.TryGetValue(cNativeId, out var cConfiguredId))
                    ShardProxy.Tell(new DeviceStateUpdate(cConfiguredId, "presence", false));
            }
        }

        Log.Debug("UniFi poll: {Count} clients connected", clients.Count);
    }

    private async Task PollCamerasAsync()
    {
        if (_protectClient == null) return;

        List<UniFiCamera> cameras;
        try
        {
            cameras = await _protectClient.GetCamerasAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to poll Protect cameras (API may not be available)");
            return;
        }

        foreach (var camera in cameras)
        {
            var mac = camera.Mac;
            if (string.IsNullOrEmpty(mac)) continue;

            var nativeId = $"protect-{mac}";
            var capabilities = new List<CapabilityDescriptor>
            {
                new() { Key = "camera", Label = "Camera", Unit = UnitType.Url },
                new() { Key = "cameraModel", Label = "Model", Unit = UnitType.Text },
                new() { Key = "cameraState", Label = "State", Unit = UnitType.Text },
            };
            var metadata = BuildCameraMetadata(camera);

            bool isNew = !_camerasByMac.ContainsKey(mac);

            if (isNew)
            {
                var id = Guid.NewGuid();
                _camerasByMac[mac] = (id, camera);
                Log.Info("Discovered UniFi Protect camera: {Name} ({Mac})", camera.Name ?? mac, mac);
            }
            else
            {
                var (existingId, _) = _camerasByMac[mac];
                _camerasByMac[mac] = (existingId, camera);
            }

            var (deviceId, _) = _camerasByMac[mac];
            Discover(deviceId, nativeId, capabilities, metadata);

            if (_configuredDevices.TryGetValue(nativeId, out var configuredId))
            {
                var isConnected = string.Equals(camera.State, "CONNECTED", StringComparison.OrdinalIgnoreCase);
                if (!isConnected)
                {
                    ReportOffline(configuredId);
                }
                else
                {
                    var rtspUrl = await GetCameraRtspUrlAsync(camera.Id);
                    ShardProxy.Tell(new DeviceStateUpdate(configuredId, "camera", rtspUrl ?? ""));

                    if (!string.IsNullOrEmpty(camera.ModelKey)) ShardProxy.Tell(new DeviceStateUpdate(configuredId, "cameraModel", camera.ModelKey));
                    if (!string.IsNullOrEmpty(camera.State)) ShardProxy.Tell(new DeviceStateUpdate(configuredId, "cameraState", camera.State));
                }
            }
        }

        Log.Debug("UniFi Protect poll: {Count} cameras", cameras.Count);
    }

    private async Task<string?> GetCameraRtspUrlAsync(string cameraId)
    {
        try
        {
            var streams = await _protectClient!.GetRtspStreamsAsync(cameraId);
            return streams.FirstOrDefault()?.Uri;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get RTSP URL for camera {CameraId}", cameraId);
            return null;
        }
    }

    private static Dictionary<string, string> BuildCameraMetadata(UniFiCamera camera)
    {
        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(camera.Name)) metadata["name"] = camera.Name;
        if (!string.IsNullOrEmpty(camera.ModelKey)) metadata["model"] = camera.ModelKey;
        if (!string.IsNullOrEmpty(camera.State)) metadata["state"] = camera.State;
        metadata["device_category"] = "camera";
        return metadata;
    }

    private async Task HandleCommandAsync(DeviceCommand cmd)
    {
        if (_client == null)
        {
            Log.Warning("Command received but UniFi is not configured yet");
            return;
        }

        try
        {
            // Find device by native ID (MAC)
            var entry = _devicesByMac.GetValueOrDefault(cmd.NativeId);
            if (entry == default)
            {
                Log.Warning("Unknown device for command: {NativeId}", cmd.NativeId);
                return;
            }

            if (cmd.CapabilityKey == "power")
            {
                // Value should be port index for power cycle
                if (cmd.Value is int portIdx || (cmd.Value is long pl && (portIdx = (int)pl) >= 0))
                {
                    var success = await _client.PowerCyclePortAsync(_siteId, entry.Device.Id, portIdx);
                    Log.Info("Power cycle port {Port} on {Device}: {Result}",
                        portIdx, cmd.NativeId, success ? "success" : "failed");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle command for device {NativeId}", cmd.NativeId);
        }
    }

    private static List<CapabilityDescriptor> BuildDeviceCapabilities(UniFiNetworkDevice device)
    {
        return
        [
            new() { Key = "uptime", Label = "Uptime", Unit = UnitType.Number },
            new() { Key = "cpuUtilization", Label = "CPU", Unit = UnitType.Percent },
            new() { Key = "memoryUtilization", Label = "Memory", Unit = UnitType.Percent },
            new() { Key = "loadAverage", Label = "Load", Unit = UnitType.Number },
            new() { Key = "txRate", Label = "TX Rate", Unit = UnitType.Number },
            new() { Key = "rxRate", Label = "RX Rate", Unit = UnitType.Number },
        ];
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
