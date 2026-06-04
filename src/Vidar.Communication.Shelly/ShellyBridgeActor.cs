using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.Shelly;

public sealed class ShellyBridgeActor : ReceiveActor, IWithTimers
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ShellyHttpClient _httpClient;
    private readonly IActorRef _shardProxy;
    private readonly Dictionary<string, ShellyDevice> _devices = new();

    public ITimerScheduler Timers { get; set; } = null!;

    private sealed class PollTick { public static readonly PollTick Instance = new(); }

    public static Props Props(ShellyHttpClient httpClient, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new ShellyBridgeActor(httpClient, shardProxy));

    public ShellyBridgeActor(ShellyHttpClient httpClient, IActorRef shardProxy)
    {
        _httpClient = httpClient;
        _shardProxy = shardProxy;

        Receive<RegisterShellyDevice>(msg =>
        {
            _devices[msg.Device.NativeId] = msg.Device;
            _log.Info("Registered Shelly device: {NativeId} at {Host}", msg.Device.NativeId, msg.Device.Host);
        });

        Receive<RegisterDeviceForPolling>(msg =>
        {
            if (msg.CommunicationType != "shelly") return;
            var device = new ShellyDevice
            {
                NativeId = msg.NativeId,
                Host = msg.Host,
                Generation = msg.Generation,
                Capabilities = msg.Capabilities,
                VidarDeviceId = msg.DeviceId
            };
            _devices[device.NativeId] = device;
            _log.Info("Registered configured Shelly device: {NativeId} at {Host} (Gen{Gen})",
                msg.NativeId, msg.Host, msg.Generation);
        });

        Receive<PollTick>(_ => PollAllDevices());
        Receive<PollFailed>(HandlePollFailed);

        ReceiveAsync<DiscoverShellyDevice>(async msg =>
        {
            _log.Info("Manual Shelly discovery requested for host {Host}", msg.Host);
            try
            {
                var statusDoc = await _httpClient.GetStatusAsync(msg.Host);
                var infoDoc = await _httpClient.GetDeviceInfoAsync(msg.Host);

                if (statusDoc == null)
                {
                    _log.Warning("Could not reach Shelly device at {Host}", msg.Host);
                    return;
                }

                var root = statusDoc.RootElement;
                var capabilities = new List<CapabilityType>();

                if (root.TryGetProperty("switch:0", out _))
                {
                    capabilities.Add(CapabilityType.Switch);
                    capabilities.Add(CapabilityType.Power);
                    capabilities.Add(CapabilityType.Energy);
                }
                if (root.TryGetProperty("cover:0", out _))
                    capabilities.Add(CapabilityType.Cover);
                if (root.TryGetProperty("temperature:0", out _))
                    capabilities.Add(CapabilityType.Temperature);
                if (root.TryGetProperty("humidity:0", out _))
                    capabilities.Add(CapabilityType.Humidity);

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
                var discovered = new DeviceDiscovered(deviceId, "shelly", nativeId, capabilities, metadata);

                var mediator = DistributedPubSub.Get(Context.System).Mediator;
                mediator.Tell(new Publish("device-discovered", discovered));

                var device = new ShellyDevice
                {
                    NativeId = nativeId,
                    Host = msg.Host,
                    Capabilities = capabilities,
                    VidarDeviceId = deviceId
                };
                Self.Tell(new RegisterShellyDevice(device));

                _log.Info("Shelly device discovered at {Host}: NativeId={NativeId}, Capabilities={Caps}",
                    msg.Host, nativeId, string.Join(",", capabilities));
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to probe Shelly device at {Host}", msg.Host);
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
                _log.Warning("No Shelly device found for DeviceId {DeviceId} / NativeId {NativeId}",
                    cmd.DeviceId, cmd.NativeId);
                return;
            }

            try
            {
                var numericValue = CoerceToInt(cmd.Value);
                var boolValue = CoerceToBool(cmd.Value);

                if (device.Generation == 1)
                {
                    if (cmd.Capability == CapabilityType.Switch && boolValue.HasValue)
                        await _httpClient.Gen1SetSwitchAsync(device.Host, 0, boolValue.Value);
                    else if (cmd.Capability == CapabilityType.Cover)
                    {
                        if (numericValue.HasValue)
                            await _httpClient.Gen1SetCoverPositionAsync(device.Host, numericValue.Value);
                        else if (cmd.Value is string dir)
                        {
                            if (dir == "open") await _httpClient.Gen1OpenCoverAsync(device.Host);
                            else if (dir == "close") await _httpClient.Gen1CloseCoverAsync(device.Host);
                        }
                    }
                }
                else
                {
                    if (cmd.Capability == CapabilityType.Switch && boolValue.HasValue)
                        await _httpClient.SetSwitchAsync(device.Host, 0, boolValue.Value);
                    else if (cmd.Capability == CapabilityType.Cover && numericValue.HasValue)
                        await _httpClient.SetCoverPositionAsync(device.Host, 0, numericValue.Value);
                }

                _log.Info("Executed {Capability} command on {NativeId} (Gen{Gen})", cmd.Capability, device.NativeId, device.Generation);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to execute command on Shelly device {Host}", device.Host);
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("commands.shelly", Self));
        mediator.Tell(new Subscribe("discover.shelly", Self));
        mediator.Tell(new Subscribe("register.shelly", Self));
        Timers.StartPeriodicTimer("poll", PollTick.Instance, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void PollAllDevices()
    {
        foreach (var device in _devices.Values)
        {
            if (device.VidarDeviceId == null) continue;
            PollDeviceAsync(device).PipeTo(Self, failure: ex => new PollFailed(device.NativeId, ex));
        }
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

        foreach (var cap in device.Capabilities)
        {
            List<ShellyCapabilityValue> updates;
            switch (cap)
            {
                case CapabilityType.Switch:
                case CapabilityType.Power:
                case CapabilityType.Energy:
                    if (root.TryGetProperty("switch:0", out var sw))
                    {
                        updates = ShellyStateMapper.MapSwitchStatus(sw);
                        SendUpdates(deviceId, updates);
                    }
                    break;
                case CapabilityType.Cover:
                    if (root.TryGetProperty("cover:0", out var cover))
                    {
                        updates = ShellyStateMapper.MapCoverStatus(cover);
                        SendUpdates(deviceId, updates);
                    }
                    break;
                case CapabilityType.Temperature:
                    if (root.TryGetProperty("temperature:0", out var temp))
                    {
                        updates = ShellyStateMapper.MapTemperatureStatus(temp);
                        SendUpdates(deviceId, updates);
                    }
                    break;
            }
        }
    }

    private async Task PollGen1DeviceAsync(ShellyDevice device, Guid deviceId)
    {
        var doc = await _httpClient.GetGen1StatusAsync(device.Host);
        if (doc == null) return;

        var root = doc.RootElement;
        bool temperatureSent = false;

        foreach (var cap in device.Capabilities)
        {
            switch (cap)
            {
                case CapabilityType.Cover:
                case CapabilityType.Power:
                    if (root.TryGetProperty("rollers", out var rollers) &&
                        rollers.ValueKind == JsonValueKind.Array &&
                        rollers.GetArrayLength() > 0)
                    {
                        var updates = ShellyStateMapper.MapGen1RollerStatus(rollers[0]);
                        SendUpdates(deviceId, updates);
                    }
                    break;
                case CapabilityType.Temperature:
                    if (!temperatureSent)
                    {
                        var updates = ShellyStateMapper.MapGen1Temperature(root);
                        SendUpdates(deviceId, updates);
                        temperatureSent = true;
                    }
                    break;
            }
        }
    }

    private void SendUpdates(Guid deviceId, List<ShellyCapabilityValue> updates)
    {
        foreach (var u in updates)
            _shardProxy.Tell(new DeviceStateUpdate(deviceId, u.Capability, u.Value));
    }

    private void HandlePollFailed(PollFailed msg)
    {
        _log.Warning(msg.Exception, "Failed to poll Shelly device {NativeId}", msg.NativeId);
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
