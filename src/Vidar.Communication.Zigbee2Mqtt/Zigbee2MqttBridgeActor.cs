using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using System.Security;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Zigbee2Mqtt;

public sealed class Zigbee2MqttBridgeActor : PluginActorBase
{
    protected override string PluginId => "zigbee2mqtt";

    private readonly IMaterializer _materializer;
    private readonly Zigbee2MqttConfig _defaultConfig;
    private Zigbee2MqttConfig _config;

    // Z2M device tracking
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByFriendlyName = new();
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByIeeeAddress = new();

    // Config cache: ieeeAddress -> configured device GUID
    private readonly Dictionary<string, Guid> _configuredDevices = new();

    // MQTT client
    private HiveMQClient? _mqttClient;

    // Stream channels
    private Channel<MqttMessage>? _inboundChannel;
    private Channel<DeviceCommand>? _outboundChannel;

    private sealed record MqttMessage(string Topic, string Payload);
    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class CheckConnection { public static readonly CheckConnection Instance = new(); }
    private sealed class RepublishDiscoveries { public static readonly RepublishDiscoveries Instance = new(); }
    public static Props Props(Zigbee2MqttConfig config) =>
        Akka.Actor.Props.Create(() => new Zigbee2MqttBridgeActor(config));

    public Zigbee2MqttBridgeActor(Zigbee2MqttConfig config)
    {
        _materializer = Context.Materializer();
        _defaultConfig = config;
        _config = config;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());

        Receive<CheckConnection>(_ =>
        {
            if (_mqttClient == null || !_mqttClient.IsConnected())
            {
                Log.Warning("MQTT client not connected, reconnecting...");
                Self.Tell(ConnectToBroker.Instance);
            }
        });

        // Device list from inbound stream
        Receive<ProcessDeviceList>(msg => HandleDeviceList(msg.Payload));

        // Outbound: receive commands from Pub/Sub, write to outbound channel
        Receive<DeviceCommand>(cmd =>
        {
            _outboundChannel?.Writer.TryWrite(cmd);
        });

        // Discovery republish
        Receive<RepublishDiscoveries>(_ => PublishDiscoveries());
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        Log.Info("Plugin registered with {Count} devices", registrations.Count);

        if (enabled)
        {
            ApplySettings(settings);
            Self.Tell(ConnectToBroker.Instance);
        }
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        Log.Info("Received config change for zigbee2mqtt, enabled={Enabled}", enabled);

        if (!enabled)
        {
            _ = DisconnectAsync();
            PublishStatus("stopped", _devicesByIeeeAddress.Count);
            return;
        }

        ApplySettings(settings);
        Self.Tell(ConnectToBroker.Instance);
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId,
        RegisterDeviceForPolling registration)
    {
        var isNew = !_configuredDevices.TryGetValue(nativeId, out var existing) || existing != deviceId;
        _configuredDevices[nativeId] = deviceId;

        if (isNew)
        {
            Log.Info("Config cache updated: {NativeId} -> {DeviceId}", nativeId, deviceId);
            if (_devicesByIeeeAddress.TryGetValue(nativeId, out var device))
                RequestDeviceState(device);
        }
    }

    private void ApplySettings(Dictionary<string, string> s)
    {
        _config = new Zigbee2MqttConfig(
            MqttHost: s.GetValueOrDefault("mqttHost") ?? _defaultConfig.MqttHost,
            MqttPort: int.TryParse(s.GetValueOrDefault("mqttPort"), out var p) ? p : _defaultConfig.MqttPort,
            MqttUser: s.GetValueOrDefault("mqttUser") ?? _defaultConfig.MqttUser,
            MqttPassword: s.GetValueOrDefault("mqttPassword") ?? _defaultConfig.MqttPassword,
            BaseTopic: s.GetValueOrDefault("baseTopic") ?? _defaultConfig.BaseTopic);
    }

    protected override void PreStart()
    {
        base.PreStart();
        Self.Tell(ConnectToBroker.Instance);
        Timers.StartPeriodicTimer("republish", RepublishDiscoveries.Instance,
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        Timers.StartPeriodicTimer("health", CheckConnection.Instance,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15));
    }

    protected override void PostStop()
    {
        _inboundChannel?.Writer.TryComplete();
        _outboundChannel?.Writer.TryComplete();
        _mqttClient?.DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        base.PostStop();
    }

    private async Task DisconnectAsync()
    {
        if (_mqttClient != null && _mqttClient.IsConnected())
        {
            try { await _mqttClient.DisconnectAsync(); } catch { /* ignore */ }
        }
        Timers.CancelAll();
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
            _inboundChannel?.Writer.TryComplete();
            _outboundChannel?.Writer.TryComplete();

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

            // Create channels
            _inboundChannel = Channel.CreateBounded<MqttMessage>(1000);
            _outboundChannel = Channel.CreateBounded<DeviceCommand>(100);

            // Connect MQTT
            _mqttClient = new HiveMQClient(optionsBuilder.Build());
            var inbound = _inboundChannel;
            _mqttClient.OnMessageReceived += (_, e) =>
            {
                var topic = e.PublishMessage.Topic;
                var payload = e.PublishMessage.PayloadAsString;
                if (topic != null && payload != null)
                    inbound.Writer.TryWrite(new MqttMessage(topic, payload));
            };

            var self = Self;
            _mqttClient.OnDisconnectReceived += (_, _) =>
            {
                Log.Warning("MQTT disconnected, will reconnect...");
                self.Tell(ConnectToBroker.Instance);
            };

            var connectResult = await _mqttClient.ConnectAsync();
            Log.Info("MQTT connect result: {Result}", connectResult.ReasonCode);

            await _mqttClient.SubscribeAsync($"{_config.BaseTopic}/#", QualityOfService.AtLeastOnceDelivery);
            Log.Info("Connected to MQTT broker at {Host}:{Port}, subscribed to {BaseTopic}/#",
                _config.MqttHost, _config.MqttPort, _config.BaseTopic);

            // Start streams
            StartInboundStream();
            StartOutboundStream();

            PublishStatus("running", _devicesByIeeeAddress.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to MQTT broker at {Host}:{Port}", _config.MqttHost, _config.MqttPort);
            PublishStatus("error", _devicesByIeeeAddress.Count, ex.Message);
        }
    }

    private void StartInboundStream()
    {
        var prefix = _config.BaseTopic + "/";
        var materializer = _materializer;
        var bridgeActor = Self;

        ChannelSource.FromReader<MqttMessage>(_inboundChannel!.Reader)
            .Where(msg => msg.Topic.StartsWith(prefix))
            .Select(msg => (Relative: msg.Topic[prefix.Length..], msg.Payload))
            .Via(Flow.Create<(string Relative, string Payload)>()
                .Select(t =>
                {
                    // Bridge messages: handle device list, skip the rest
                    if (t.Relative == "bridge/devices")
                    {
                        bridgeActor.Tell(new ProcessDeviceList(t.Payload));
                        return (Updates: (List<(Guid DeviceId, string CapabilityKey, object Value)>?)null, t.Relative);
                    }
                    if (t.Relative.StartsWith("bridge/") || t.Relative.Contains("/set") ||
                        t.Relative.Contains("/get") || t.Relative.Contains("/availability"))
                        return (Updates: null, t.Relative);

                    // Device state: parse, enrich with configured ID, produce updates
                    if (!_devicesByFriendlyName.TryGetValue(t.Relative, out var device))
                        return (Updates: null, t.Relative);

                    var ieeeAddress = device.IeeeAddress;
                    if (!_configuredDevices.TryGetValue(ieeeAddress, out var deviceId))
                        return (Updates: null, t.Relative);

                    var mapped = Zigbee2MqttStateMapper.MapState(t.Payload, device.Capabilities);
                    var updates = mapped.Select(u => (deviceId, u.CapabilityKey, u.Value)).ToList();
                    return (Updates: (List<(Guid, string, object)>?)updates, t.Relative);
                })
                .Where(t => t.Updates != null))
            .SelectMany(t => t.Updates!)
            .To(Sink.ForEach<(Guid DeviceId, string CapabilityKey, object Value)>(u =>
                ShardProxy.Tell(new DeviceStateUpdate(u.DeviceId, u.CapabilityKey, u.Value))))
            .Run(materializer);
    }

    private void StartOutboundStream()
    {
        var mqttClient = _mqttClient!;
        var baseTopic = _config.BaseTopic;
        var materializer = _materializer;

        ChannelSource.FromReader<DeviceCommand>(_outboundChannel!.Reader)
            .Select(cmd =>
            {
                // Find device by configured GUID
                var device = _devicesByIeeeAddress.Values.FirstOrDefault(d =>
                    _configuredDevices.TryGetValue(d.IeeeAddress, out var id) && id == cmd.DeviceId);
                if (device == null) return (Topic: (string?)null, Payload: (string?)null);

                var payload = BuildCommandPayload(cmd.CapabilityKey, cmd.Value);
                if (payload == null) return (Topic: null, Payload: null);

                return (Topic: (string?)$"{baseTopic}/{device.FriendlyName}/set", Payload: (string?)payload);
            })
            .Where(t => t.Topic != null)
            .SelectAsync(1, async t =>
            {
                await mqttClient.PublishAsync(t.Topic!, t.Payload!, QualityOfService.AtMostOnceDelivery);
                Log.Info("Sent command to {Topic}: {Payload}", t.Topic, t.Payload);
                return t;
            })
            .To(Sink.Ignore<(string?, string?)>())
            .Run(materializer);
    }

    // --- Device list handling (called from inbound stream via actor message) ---

    private sealed record ProcessDeviceList(string Payload);

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

                if (deviceEl.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "Coordinator")
                    continue;

                var capabilities = new List<CapabilityDescriptor>();
                var lightFeatures = new HashSet<string>();
                var actionValues = new List<string>();
                if (deviceEl.TryGetProperty("definition", out var definition) &&
                    definition.ValueKind != JsonValueKind.Null &&
                    definition.TryGetProperty("exposes", out var exposes))
                {
                    capabilities = ExposesMapper.MapCapabilities(exposes);
                    lightFeatures = ExposesMapper.ExtractLightFeatures(exposes);
                    actionValues = ExposesMapper.ExtractActionValues(exposes);
                }

                if (!capabilities.Any(c => c.Key == "update") &&
                    deviceEl.TryGetProperty("software_build_id", out _))
                    capabilities.Add(new CapabilityDescriptor { Key = "update", Label = "Update", Unit = UnitType.Text });

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
                if (actionValues.Count > 0)
                    metadata["action_values"] = string.Join(",", actionValues);

                if (_devicesByIeeeAddress.TryGetValue(ieeeAddress, out var existing))
                {
                    existing.Capabilities.Clear();
                    existing.Capabilities.AddRange(capabilities);
                    existing.Metadata = metadata;
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
                    };
                    _devicesByIeeeAddress[ieeeAddress] = device;
                    _devicesByFriendlyName[friendlyName] = device;
                }
            }

            Log.Info("Z2M device list processed: {Count} devices tracked", _devicesByIeeeAddress.Count);
            PublishStatus("running", _devicesByIeeeAddress.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse Zigbee2MQTT device list");
        }
    }

    private void PublishDiscoveries()
    {
        if (_devicesByIeeeAddress.Count == 0) return;
        foreach (var device in _devicesByIeeeAddress.Values)
        {
            var id = _configuredDevices.GetValueOrDefault(device.IeeeAddress, Guid.NewGuid());
            Discover(id, device.IeeeAddress, device.Capabilities, device.Metadata);
        }
    }

    private void RequestDeviceState(Zigbee2MqttDevice device)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected()) return;
        if (!device.Capabilities.Any(c => c.Key == "light") &&
            !device.Capabilities.Any(c => c.Key == "switch") &&
            !device.Capabilities.Any(c => c.Key == "cover"))
            return;
        var topic = $"{_config.BaseTopic}/{device.FriendlyName}/get";
        _ = _mqttClient.PublishAsync(topic, "{\"state\":\"\"}", QualityOfService.AtMostOnceDelivery);
    }

    private static string? BuildCommandPayload(string capabilityKey, object value)
    {
        return capabilityKey switch
        {
            "switch" => JsonSerializer.Serialize(new { state = CoerceToBool(value) == true ? "ON" : "OFF" }),
            "dimmer" => CoerceToNumber(value) is { } b ? JsonSerializer.Serialize(new { brightness = (int)(b / 100.0 * 254.0) }) : null,
            "light" => BuildLightPayload(value),
            "cover" => CoerceToNumber(value) is { } p ? JsonSerializer.Serialize(new { position = (int)p }) : null,
            "update" => BuildUpdatePayload(value),
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

    private static string BuildUpdatePayload(object value)
    {
        var action = value switch
        {
            string s => s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString() ?? "update",
            _ => "update"
        };
        return JsonSerializer.Serialize(new { update = new { state = action } });
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
