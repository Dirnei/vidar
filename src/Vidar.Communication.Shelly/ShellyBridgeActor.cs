using Akka.Actor;
using Akka.Event;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Shelly;

public sealed class ShellyBridgeActor : PluginActorBase
{
    private readonly ShellyHttpClient _httpClient;
    private readonly Dictionary<string, ShellyDevice> _devices = new();

    private sealed class PollTick { public static readonly PollTick Instance = new(); }

    protected override string PluginId => "shelly";

    public static Props Props(ShellyHttpClient httpClient, IActorRef pluginRegistry, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new ShellyBridgeActor(httpClient, pluginRegistry, shardProxy));

    public ShellyBridgeActor(ShellyHttpClient httpClient, IActorRef pluginRegistry, IActorRef shardProxy)
        : base(pluginRegistry, shardProxy)
    {
        _httpClient = httpClient;

        Receive<RegisterShellyDevice>(msg =>
        {
            _devices[msg.Device.NativeId] = msg.Device;
            Log.Info("Registered Shelly device: {NativeId} at {Host}", msg.Device.NativeId, msg.Device.Host);
        });

        Receive<PollTick>(_ => PollAllDevices());
        Receive<PollFailed>(HandlePollFailed);

        ReceiveAsync<DiscoverShellyDevice>(async msg =>
        {
            Log.Info("Manual Shelly discovery requested for host {Host}", msg.Host);
            try
            {
                var statusDoc = await _httpClient.GetStatusAsync(msg.Host);
                var infoDoc = await _httpClient.GetDeviceInfoAsync(msg.Host);

                if (statusDoc == null)
                {
                    Log.Warning("Could not reach Shelly device at {Host}", msg.Host);
                    return;
                }

                var root = statusDoc.RootElement;
                var capabilities = new List<CapabilityDescriptor>();

                if (root.TryGetProperty("switch:0", out _))
                {
                    capabilities.Add(new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true });
                    capabilities.Add(new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.Watts });
                    capabilities.Add(new CapabilityDescriptor { Key = "energy", Label = "Energy", Unit = UnitType.WattHours });
                }
                if (root.TryGetProperty("cover:0", out _))
                    capabilities.Add(new CapabilityDescriptor { Key = "cover", Label = "Cover", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 });
                if (root.TryGetProperty("temperature:0", out _))
                    capabilities.Add(new CapabilityDescriptor { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius });
                if (root.TryGetProperty("humidity:0", out _))
                    capabilities.Add(new CapabilityDescriptor { Key = "humidity", Label = "Humidity", Unit = UnitType.Percent });

                var metadata = new Dictionary<string, string> { ["host"] = msg.Host };
                string nativeId = msg.Host;

                if (infoDoc != null)
                {
                    var info = infoDoc.RootElement;
                    if (info.TryGetProperty("id", out var idProp))
                        nativeId = idProp.GetString() ?? msg.Host;
                    if (info.TryGetProperty("model", out var model))
                        metadata["model"] = model.GetString() ?? string.Empty;
                    if (info.TryGetProperty("fw_id", out var fw))
                        metadata["firmware"] = fw.GetString() ?? string.Empty;
                }

                var deviceId = Guid.NewGuid();
                Discover(deviceId, nativeId, capabilities, metadata);

                var device = new ShellyDevice
                {
                    NativeId = nativeId,
                    Host = msg.Host,
                    Capabilities = capabilities,
                    VidarDeviceId = deviceId
                };
                Self.Tell(new RegisterShellyDevice(device));

                Log.Info("Shelly device discovered at {Host}: NativeId={NativeId}, Capabilities={Caps}",
                    msg.Host, nativeId, string.Join(",", capabilities.Select(c => c.Key)));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to probe Shelly device at {Host}", msg.Host);
            }
        });

        ReceiveAsync<DeviceCommand>(async cmd =>
        {
            // Match by NativeId (most specific) or fall back to VidarDeviceId
            var device = _devices.TryGetValue(cmd.NativeId, out var byNative)
                ? byNative
                : _devices.Values.FirstOrDefault(d => d.VidarDeviceId == cmd.DeviceId);

            if (device == null)
            {
                Log.Warning("No Shelly device found for DeviceId {DeviceId} / NativeId {NativeId}",
                    cmd.DeviceId, cmd.NativeId);
                return;
            }

            try
            {
                Log.Info("Command value type: {Type}, value: {Value}", cmd.Value?.GetType().FullName ?? "null", cmd.Value);
                var numericValue = cmd.Value != null ? CoerceToInt(cmd.Value) : null;
                var boolValue = cmd.Value != null ? CoerceToBool(cmd.Value) : null;
                Log.Info("Coerced: numeric={Numeric}, bool={Bool}", numericValue?.ToString() ?? "null", boolValue?.ToString() ?? "null");

                if (device.Generation == 1)
                {
                    if (cmd.CapabilityKey == "switch" && boolValue.HasValue)
                        await _httpClient.Gen1SetSwitchAsync(device.Host, 0, boolValue.Value);
                    else if (cmd.CapabilityKey == "cover")
                    {
                        if (numericValue.HasValue)
                        {
                            var result = await _httpClient.Gen1SetCoverPositionAsync(device.Host, numericValue.Value);
                            Log.Info("Gen1 cover response from {Host}: {Response}", device.Host, result);
                        }
                        else if (cmd.Value is string dir)
                        {
                            if (dir == "open") await _httpClient.Gen1OpenCoverAsync(device.Host);
                            else if (dir == "close") await _httpClient.Gen1CloseCoverAsync(device.Host);
                        }
                    }
                }
                else
                {
                    if (cmd.CapabilityKey == "switch" && boolValue.HasValue)
                        await _httpClient.SetSwitchAsync(device.Host, 0, boolValue.Value);
                    else if (cmd.CapabilityKey == "cover" && numericValue.HasValue)
                        await _httpClient.SetCoverPositionAsync(device.Host, 0, numericValue.Value);
                }

                Log.Info("Executed {CapabilityKey} command on {NativeId} (Gen{Gen})", cmd.CapabilityKey, device.NativeId, device.Generation);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to execute command on Shelly device {Host}", device.Host);
            }
        });
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        foreach (var reg in registrations)
        {
            var device = new ShellyDevice
            {
                NativeId = reg.NativeId,
                Host = reg.Host,
                Generation = reg.Generation,
                Capabilities = reg.Capabilities,
                VidarDeviceId = reg.DeviceId
            };
            _devices[device.NativeId] = device;
        }
        Log.Info("Plugin registered with {Count} devices, enabled={Enabled}", registrations.Count, enabled);
        PublishStatus("running", _devices.Count);
        if (enabled)
            Timers.StartPeriodicTimer("poll", PollTick.Instance, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void PollAllDevices()
    {
        foreach (var device in _devices.Values)
        {
            if (device.VidarDeviceId == null) continue;
            PollDeviceAsync(device).PipeTo(Self, failure: ex => new PollFailed(device.NativeId, ex));
        }

        PublishStatus("running", _devices.Count);
    }

    private async Task PollDeviceAsync(ShellyDevice device)
    {
        var deviceId = device.VidarDeviceId!.Value;

        if (device.Generation == 1)
        {
            await PollGen1DeviceAsync(device, deviceId);
        }
        else
        {
            await PollGen2DeviceAsync(device, deviceId);
        }
    }

    private async Task PollGen2DeviceAsync(ShellyDevice device, Guid deviceId)
    {
        var doc = await _httpClient.GetStatusAsync(device.Host);
        if (doc == null) return;

        var root = doc.RootElement;
        var keys = device.Capabilities.Select(c => c.Key).ToHashSet();

        if (keys.Contains("switch") || keys.Contains("power") || keys.Contains("energy"))
        {
            if (root.TryGetProperty("switch:0", out var sw))
                SendUpdates(deviceId, ShellyStateMapper.MapSwitchStatus(sw));
            else if (root.TryGetProperty("pm1:0", out var pm))
                SendUpdates(deviceId, ShellyStateMapper.MapSwitchStatus(pm));
        }

        if (keys.Contains("cover"))
        {
            if (root.TryGetProperty("cover:0", out var cover))
                SendUpdates(deviceId, ShellyStateMapper.MapCoverStatus(cover));
        }

        if (keys.Contains("temperature"))
        {
            if (root.TryGetProperty("temperature:0", out var temp))
                SendUpdates(deviceId, ShellyStateMapper.MapTemperatureStatus(temp));
        }
    }

    private async Task PollGen1DeviceAsync(ShellyDevice device, Guid deviceId)
    {
        var doc = await _httpClient.GetGen1StatusAsync(device.Host);
        if (doc == null) return;

        var root = doc.RootElement;
        var keys = device.Capabilities.Select(c => c.Key).ToHashSet();
        bool temperatureSent = false;

        if (keys.Contains("cover") || keys.Contains("power"))
        {
            if (root.TryGetProperty("rollers", out var rollers) &&
                rollers.ValueKind == JsonValueKind.Array &&
                rollers.GetArrayLength() > 0)
            {
                SendUpdates(deviceId, ShellyStateMapper.MapGen1RollerStatus(rollers[0]));
            }
        }

        if (keys.Contains("temperature") && !temperatureSent)
        {
            SendUpdates(deviceId, ShellyStateMapper.MapGen1Temperature(root));
            temperatureSent = true;
        }
    }

    private void SendUpdates(Guid deviceId, List<ShellyCapabilityValue> updates)
    {
        foreach (var u in updates)
            ReportState(deviceId, u.CapabilityKey, u.Value);
    }

    private void HandlePollFailed(PollFailed msg)
    {
        Log.Warning(msg.Exception, "Failed to poll Shelly device {NativeId}", msg.NativeId);
        if (_devices.TryGetValue(msg.NativeId, out var device) && device.VidarDeviceId.HasValue)
        {
            ReportOffline(device.VidarDeviceId.Value);
        }
    }

    private static int? CoerceToInt(object value) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.Number => el.GetInt32(),
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => null
    };

    private static bool? CoerceToBool(object value) => value switch
    {
        bool b => b,
        System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.False => false,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => null
    };

    private sealed record PollFailed(string NativeId, Exception Exception);
}

public sealed record RegisterShellyDevice(ShellyDevice Device);
