using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using HiveMQtt.Client;
using HiveMQtt.Client.Events;
using HiveMQtt.MQTT5.Types;
using System.Security;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.Zigbee2Mqtt;

public sealed class Zigbee2MqttBridgeActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;
    private sealed class RepublishDiscoveries { public static readonly RepublishDiscoveries Instance = new(); }
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Zigbee2MqttConfig _config;
    private readonly IActorRef _shardProxy;
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByFriendlyName = new();
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByIeeeAddress = new();
    private HiveMQClient? _mqttClient;

    private sealed record MqttMessageReceived(string Topic, string Payload);
    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class CheckConnection { public static readonly CheckConnection Instance = new(); }

    public static Props Props(Zigbee2MqttConfig config, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new Zigbee2MqttBridgeActor(config, shardProxy));

    public Zigbee2MqttBridgeActor(Zigbee2MqttConfig config, IActorRef shardProxy)
    {
        _config = config;
        _shardProxy = shardProxy;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());
        Receive<CheckConnection>(_ =>
        {
            if (_mqttClient == null || !_mqttClient.IsConnected())
            {
                _log.Warning("MQTT client not connected, reconnecting...");
                Self.Tell(ConnectToBroker.Instance);
            }
        });
        Receive<MqttMessageReceived>(msg => HandleMqttMessage(msg.Topic, msg.Payload));

        Receive<RegisterDeviceForPolling>(msg =>
        {
            if (msg.CommunicationType != "zigbee2mqtt") return;
            // NativeId = IEEE address, Host = friendly_name (overloaded field)
            if (_devicesByIeeeAddress.TryGetValue(msg.NativeId, out var device))
            {
                device.VidarDeviceId = msg.DeviceId;
                _log.Info("Z2M device {FriendlyName} ({IeeeAddress}) mapped to configured ID {DeviceId}",
                    device.FriendlyName, msg.NativeId, msg.DeviceId);
                RequestDeviceState(device);
            }
            else
            {
                _log.Info("Z2M registration for unknown IEEE {IeeeAddress}, will map when device list arrives", msg.NativeId);
            }
        });

        Receive<RepublishDiscoveries>(_ =>
        {
            if (_devicesByIeeeAddress.Count == 0) return;
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            foreach (var device in _devicesByIeeeAddress.Values)
            {
                if (device.VidarDeviceId == null) continue;
                var discovered = new DeviceDiscovered(
                    device.VidarDeviceId.Value, "zigbee2mqtt", device.IeeeAddress,
                    device.Capabilities, device.Metadata);
                mediator.Tell(new Publish("device-discovered", discovered));
            }
        });

        ReceiveAsync<DeviceCommand>(async cmd =>
        {
            var device = _devicesByFriendlyName.Values.FirstOrDefault(d => d.VidarDeviceId == cmd.DeviceId)
                         ?? _devicesByIeeeAddress.Values.FirstOrDefault(d => d.VidarDeviceId == cmd.DeviceId);
            if (device == null || _mqttClient == null)
            {
                _log.Warning("Cannot execute command for {DeviceId}: device={Found}, mqtt={Connected}",
                    cmd.DeviceId, device != null, _mqttClient != null);
                return;
            }

            var payload = BuildCommandPayload(cmd.Capability, cmd.Value);
            if (payload != null)
            {
                var topic = $"{_config.BaseTopic}/{device.FriendlyName}/set";
                await _mqttClient.PublishAsync(topic, payload, QualityOfService.AtMostOnceDelivery);
                _log.Info("Sent command to {Topic}: {Payload}", topic, payload);
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("commands.zigbee2mqtt", Self));
        mediator.Tell(new Subscribe("register.zigbee2mqtt", Self));
        Self.Tell(ConnectToBroker.Instance);
        Timers.StartPeriodicTimer("republish", RepublishDiscoveries.Instance,
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        Timers.StartPeriodicTimer("health", CheckConnection.Instance,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15));
    }

    protected override void PostStop()
    {
        _mqttClient?.DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        base.PostStop();
    }

    private async Task ConnectAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                try { await _mqttClient.DisconnectAsync(); } catch { }
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            var optionsBuilder = new HiveMQClientOptionsBuilder()
                .WithBroker(_config.MqttHost)
                .WithPort(_config.MqttPort)
                .WithClientId($"vidar-z2m-{Guid.NewGuid():N}");

            if (!string.IsNullOrEmpty(_config.MqttUser))
                optionsBuilder.WithUserName(_config.MqttUser);
            if (!string.IsNullOrEmpty(_config.MqttPassword))
            {
                var secure = new SecureString();
                foreach (var c in _config.MqttPassword) secure.AppendChar(c);
                secure.MakeReadOnly();
                optionsBuilder.WithPassword(secure);
            }

            _mqttClient = new HiveMQClient(optionsBuilder.Build());
            var self = Self;
            _mqttClient.OnMessageReceived += (sender, e) =>
            {
                var topic = e.PublishMessage.Topic;
                var payload = e.PublishMessage.PayloadAsString;
                if (topic != null && payload != null)
                {
                    if (!topic.Contains("/bridge/"))
                        Console.WriteLine($"[Z2M] Device msg: {topic} ({payload.Length}b)");
                    self.Tell(new MqttMessageReceived(topic, payload));
                }
            };
            _mqttClient.OnDisconnectReceived += (sender, e) =>
            {
                Console.WriteLine($"[Z2M-MQTT] Disconnected from broker, will reconnect...");
                self.Tell(ConnectToBroker.Instance);
            };

            var connectResult = await _mqttClient.ConnectAsync();
            _log.Info("MQTT connect result: {Result}", connectResult.ReasonCode);

            var subResult = await _mqttClient.SubscribeAsync($"{_config.BaseTopic}/#", QualityOfService.AtLeastOnceDelivery);
            _log.Info("MQTT subscribed to {Topic}/# ({Count} subscriptions) with QoS1", _config.BaseTopic, subResult.Subscriptions.Count);

            _log.Info("Connected to MQTT broker at {Host}:{Port}, subscribed to {BaseTopic}/#",
                _config.MqttHost, _config.MqttPort, _config.BaseTopic);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to MQTT broker at {Host}:{Port}", _config.MqttHost, _config.MqttPort);
        }
    }

    private void HandleMqttMessage(string topic, string payload)
    {
        var prefix = _config.BaseTopic + "/";

        if (!topic.StartsWith(prefix))
            return;

        var relative = topic[prefix.Length..];
        _log.Info("MQTT msg: {Relative} ({Len} bytes)", relative, payload.Length);

        if (relative == "bridge/devices")
        {
            HandleDeviceList(payload);
            return;
        }

        if (relative.StartsWith("bridge/"))
            return;
        if (relative.Contains("/set") || relative.Contains("/get") || relative.Contains("/availability"))
            return;

        if (_devicesByFriendlyName.TryGetValue(relative, out var device) && device.VidarDeviceId != null)
        {
            var updates = Zigbee2MqttStateMapper.MapState(payload, device.Capabilities);
            foreach (var u in updates)
                _shardProxy.Tell(new DeviceStateUpdate(device.VidarDeviceId.Value, u.Capability, u.Value));
        }
    }

    private void HandleDeviceList(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            foreach (var deviceEl in doc.RootElement.EnumerateArray())
            {
                if (!deviceEl.TryGetProperty("ieee_address", out var ieeeAddressProp)) continue;
                if (!deviceEl.TryGetProperty("friendly_name", out var friendlyNameProp)) continue;

                var ieeeAddress = ieeeAddressProp.GetString();
                var friendlyName = friendlyNameProp.GetString();
                if (ieeeAddress == null || friendlyName == null) continue;

                // Skip the coordinator
                if (deviceEl.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "Coordinator")
                    continue;

                var capabilities = new List<CapabilityType>();
                var lightFeatures = new HashSet<string>();
                if (deviceEl.TryGetProperty("definition", out var definition) &&
                    definition.ValueKind != JsonValueKind.Null &&
                    definition.TryGetProperty("exposes", out var exposes))
                {
                    capabilities = ExposesMapper.MapCapabilities(exposes);
                    lightFeatures = ExposesMapper.ExtractLightFeatures(exposes);
                }

                if (capabilities.Count == 0)
                    continue;

                var metadata = new Dictionary<string, string>();
                if (deviceEl.TryGetProperty("manufacturer", out var mfr) && mfr.GetString() != null)
                    metadata["manufacturer"] = mfr.GetString()!;
                if (deviceEl.TryGetProperty("model_id", out var modelId) && modelId.GetString() != null)
                    metadata["model_id"] = modelId.GetString()!;
                if (deviceEl.TryGetProperty("definition", out var def2) && def2.ValueKind != JsonValueKind.Null)
                {
                    if (def2.TryGetProperty("model", out var model) && model.GetString() != null)
                        metadata["model"] = model.GetString()!;
                    if (def2.TryGetProperty("vendor", out var vendor) && vendor.GetString() != null)
                        metadata["vendor"] = vendor.GetString()!;
                    if (def2.TryGetProperty("description", out var desc) && desc.GetString() != null)
                        metadata["description"] = desc.GetString()!;
                }
                metadata["friendly_name"] = friendlyName;
                if (lightFeatures.Count > 0)
                    metadata["light_features"] = string.Join(",", lightFeatures);

                if (_devicesByIeeeAddress.TryGetValue(ieeeAddress, out var existing))
                {
                    existing.Capabilities.Clear();
                    existing.Capabilities.AddRange(capabilities);
                    if (existing.FriendlyName != friendlyName)
                    {
                        _devicesByFriendlyName.Remove(existing.FriendlyName);
                        existing.FriendlyName = friendlyName;
                        _devicesByFriendlyName[friendlyName] = existing;
                    }
                }
                else
                {
                    var device = new Zigbee2MqttDevice
                    {
                        IeeeAddress = ieeeAddress,
                        FriendlyName = friendlyName,
                        Capabilities = capabilities,
                        Metadata = metadata,
                        VidarDeviceId = Guid.NewGuid()
                    };
                    _devicesByIeeeAddress[ieeeAddress] = device;
                    _devicesByFriendlyName[friendlyName] = device;

                    var discovered = new DeviceDiscovered(
                        device.VidarDeviceId.Value,
                        "zigbee2mqtt",
                        ieeeAddress,
                        capabilities,
                        metadata);

                    var mediator = DistributedPubSub.Get(Context.System).Mediator;
                    mediator.Tell(new Publish("device-discovered", discovered));

                    _log.Info("Discovered Z2M device: {FriendlyName} ({IeeeAddress}) caps=[{Caps}]",
                        friendlyName, ieeeAddress, string.Join(",", capabilities));
                }
            }

            _log.Info("Z2M device list processed: {Count} devices tracked", _devicesByIeeeAddress.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to parse Zigbee2MQTT device list");
        }
    }

    private void RequestDeviceState(Zigbee2MqttDevice device)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected()) return;
        var topic = $"{_config.BaseTopic}/{device.FriendlyName}/get";
        _ = _mqttClient.PublishAsync(topic, "{\"state\":\"\"}", QualityOfService.AtMostOnceDelivery);
    }

    private static string? BuildCommandPayload(CapabilityType capability, object value)
    {
        return capability switch
        {
            CapabilityType.Switch => JsonSerializer.Serialize(new { state = CoerceToBool(value) == true ? "ON" : "OFF" }),
            CapabilityType.Dimmer => CoerceToNumber(value) is { } b ? JsonSerializer.Serialize(new { brightness = (int)(b / 100.0 * 254.0) }) : null,
            CapabilityType.Light => BuildLightPayload(value),
            CapabilityType.Cover => CoerceToNumber(value) is { } p ? JsonSerializer.Serialize(new { position = (int)p }) : null,
            _ => null
        };
    }

    private static string? BuildLightPayload(object value)
    {
        if (CoerceToBool(value) is { } on)
            return JsonSerializer.Serialize(new { state = on ? "ON" : "OFF" });
        if (CoerceToNumber(value) is { } brightness)
            return JsonSerializer.Serialize(new { brightness = (int)(brightness / 100.0 * 254.0) });
        if (value is JsonElement el && el.ValueKind == JsonValueKind.Object)
            return el.GetRawText();
        if (value is Dictionary<string, object> dict)
            return JsonSerializer.Serialize(dict);
        if (value is string s)
            return s;
        return null;
    }

    private static double? CoerceToNumber(object value) => value switch
    {
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        JsonElement el when el.ValueKind == JsonValueKind.Number => el.GetDouble(),
        string s when double.TryParse(s, out var parsed) => parsed,
        _ => null
    };

    private static bool? CoerceToBool(object value) => value switch
    {
        bool b => b,
        JsonElement el when el.ValueKind == JsonValueKind.True => true,
        JsonElement el when el.ValueKind == JsonValueKind.False => false,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => null
    };
}
