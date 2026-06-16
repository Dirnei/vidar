using System.Threading.Channels;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using System.Security;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.Zigbee2Mqtt;

public sealed class Zigbee2MqttBridgeActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer;
    private readonly Zigbee2MqttConfig _defaultConfig;
    private Zigbee2MqttConfig _config;
    private readonly IActorRef _shardProxy;

    // Z2M device tracking
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByFriendlyName = new();
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByIeeeAddress = new();

    // Config cache: ieeeAddress → configured device GUID
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
    private sealed class FetchRegistrations { }
    private sealed class RequestConfig { public static readonly RequestConfig Instance = new(); }

    public static Props Props(Zigbee2MqttConfig config, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new Zigbee2MqttBridgeActor(config, shardProxy));

    public Zigbee2MqttBridgeActor(Zigbee2MqttConfig config, IActorRef shardProxy)
    {
        _materializer = Context.Materializer();
        _defaultConfig = config;
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

        // Config cache update — from Host when a device is configured
        Receive<RegisterDeviceForPolling>(msg =>
        {
            if (msg.CommunicationType != "zigbee2mqtt") return;
            var isNew = !_configuredDevices.TryGetValue(msg.NativeId, out var existing) || existing != msg.DeviceId;
            _configuredDevices[msg.NativeId] = msg.DeviceId;

            if (isNew)
            {
                _log.Info("Config cache updated: {NativeId} → {DeviceId}", msg.NativeId, msg.DeviceId);
                if (_devicesByIeeeAddress.TryGetValue(msg.NativeId, out var device))
                    RequestDeviceState(device);
            }
        });

        Receive<RegistrationResponse>(msg =>
        {
            foreach (var reg in msg.Devices)
            {
                _configuredDevices[reg.NativeId] = reg.DeviceId;
            }
            _log.Info("Config cache loaded: {Count} devices", msg.Devices.Count);

            foreach (var reg in msg.Devices)
            {
                if (_devicesByIeeeAddress.TryGetValue(reg.NativeId, out var device))
                    RequestDeviceState(device);
            }
        });

        // Device list from inbound stream
        Receive<ProcessDeviceList>(msg => HandleDeviceList(msg.Payload));

        // Fetch registrations from Host — retries until successful
        Receive<FetchRegistrations>(_ =>
        {
            if (_configuredDevices.Count > 0) return;
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Publish("request-registrations", new RequestRegistrations("zigbee2mqtt")));
            Timers.StartSingleTimer("fetch-registrations-retry", new FetchRegistrations(), TimeSpan.FromSeconds(5));
        });

        // Outbound: receive commands from Pub/Sub, write to outbound channel
        Receive<DeviceCommand>(cmd =>
        {
            _outboundChannel?.Writer.TryWrite(cmd);
        });

        // Discovery republish
        Receive<RepublishDiscoveries>(_ => PublishDiscoveries());

        // Integration config updates from ApplicationConfig system
        Receive<IntegrationConfigChanged>(msg =>
        {
            if (msg.IntegrationId != "zigbee2mqtt") return;
            _log.Info("Received IntegrationConfigChanged for zigbee2mqtt, enabled={Enabled}", msg.Enabled);

            if (!msg.Enabled)
            {
                _ = DisconnectAsync();
                PublishStatus("stopped");
                return;
            }

            var s = msg.Settings;
            _config = new Zigbee2MqttConfig(
                MqttHost: s.GetValueOrDefault("mqttHost") ?? _defaultConfig.MqttHost,
                MqttPort: int.TryParse(s.GetValueOrDefault("mqttPort"), out var p) ? p : _defaultConfig.MqttPort,
                MqttUser: s.GetValueOrDefault("mqttUser") ?? _defaultConfig.MqttUser,
                MqttPassword: s.GetValueOrDefault("mqttPassword") ?? _defaultConfig.MqttPassword,
                BaseTopic: s.GetValueOrDefault("baseTopic") ?? _defaultConfig.BaseTopic);

            Self.Tell(ConnectToBroker.Instance);
        });

        Receive<RequestConfig>(_ =>
        {
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Publish("request-integration-config", new RequestIntegrationConfig("zigbee2mqtt")));
            _log.Info("Requested integration config for zigbee2mqtt from Host");
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("commands.zigbee2mqtt", Self));
        mediator.Tell(new Subscribe("register.zigbee2mqtt", Self));
        mediator.Tell(new Subscribe("registration-response.zigbee2mqtt", Self));
        mediator.Tell(new Subscribe("integration-config.zigbee2mqtt", Self));
        Self.Tell(ConnectToBroker.Instance);
        Self.Tell(RequestConfig.Instance);
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
                _log.Warning("MQTT disconnected, will reconnect...");
                self.Tell(ConnectToBroker.Instance);
            };

            var connectResult = await _mqttClient.ConnectAsync();
            _log.Info("MQTT connect result: {Result}", connectResult.ReasonCode);

            await _mqttClient.SubscribeAsync($"{_config.BaseTopic}/#", QualityOfService.AtLeastOnceDelivery);
            _log.Info("Connected to MQTT broker at {Host}:{Port}, subscribed to {BaseTopic}/#",
                _config.MqttHost, _config.MqttPort, _config.BaseTopic);

            // Start streams
            StartInboundStream();
            StartOutboundStream();

            PublishStatus("running");

            // Request registrations from Host (delayed to let cluster form)
            Timers.StartSingleTimer("fetch-registrations", new FetchRegistrations(), TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to MQTT broker at {Host}:{Port}", _config.MqttHost, _config.MqttPort);
            PublishStatus("error", ex.Message);
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
                        return (Updates: (List<(Guid DeviceId, CapabilityType Cap, object Value)>?)null, t.Relative);
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
                    var updates = mapped.Select(u => (deviceId, u.Capability, u.Value)).ToList();
                    return (Updates: (List<(Guid, CapabilityType, object)>?)updates, t.Relative);
                })
                .Where(t => t.Updates != null))
            .SelectMany(t => t.Updates!)
            .To(Sink.ForEach<(Guid DeviceId, CapabilityType Cap, object Value)>(u =>
                _shardProxy.Tell(new DeviceStateUpdate(u.DeviceId, u.Cap, u.Value))))
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

                var payload = BuildCommandPayload(cmd.Capability, cmd.Value);
                if (payload == null) return (Topic: null, Payload: null);

                return (Topic: (string?)$"{baseTopic}/{device.FriendlyName}/set", Payload: (string?)payload);
            })
            .Where(t => t.Topic != null)
            .SelectAsync(1, async t =>
            {
                await mqttClient.PublishAsync(t.Topic!, t.Payload!, QualityOfService.AtMostOnceDelivery);
                _log.Info("Sent command to {Topic}: {Payload}", t.Topic, t.Payload);
                return t;
            })
            .To(Sink.Ignore<(string?, string?)>())
            .Run(materializer);
    }

    // --- Device list handling (called from inbound stream via actor message) ---

    private sealed record ProcessDeviceList(string Payload);

    // Register handler in constructor — need to add this
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

                var capabilities = new List<CapabilityType>();
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

                if (!capabilities.Contains(CapabilityType.Update) &&
                    deviceEl.TryGetProperty("software_build_id", out _))
                    capabilities.Add(CapabilityType.Update);

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

            _log.Info("Z2M device list processed: {Count} devices tracked", _devicesByIeeeAddress.Count);
            PublishStatus("running");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to parse Zigbee2MQTT device list");
        }
    }

    private void PublishDiscoveries()
    {
        if (_devicesByIeeeAddress.Count == 0) return;
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        foreach (var device in _devicesByIeeeAddress.Values)
        {
            var id = _configuredDevices.GetValueOrDefault(device.IeeeAddress, Guid.NewGuid());
            var discovered = new DeviceDiscovered(id, "zigbee2mqtt", device.IeeeAddress,
                device.Capabilities, device.Metadata);
            mediator.Tell(new Publish("device-discovered", discovered));
        }
    }

    private void RequestDeviceState(Zigbee2MqttDevice device)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected()) return;
        if (!device.Capabilities.Contains(CapabilityType.Light) &&
            !device.Capabilities.Contains(CapabilityType.Switch) &&
            !device.Capabilities.Contains(CapabilityType.Cover))
            return;
        var topic = $"{_config.BaseTopic}/{device.FriendlyName}/get";
        _ = _mqttClient.PublishAsync(topic, "{\"state\":\"\"}", QualityOfService.AtMostOnceDelivery);
    }

    private void PublishStatus(string status, string? error = null)
    {
        var deviceCount = _devicesByIeeeAddress.Count;
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate("zigbee2mqtt", status, deviceCount, error)));
    }

    private static string? BuildCommandPayload(CapabilityType capability, object value)
    {
        return capability switch
        {
            CapabilityType.Switch => JsonSerializer.Serialize(new { state = CoerceToBool(value) == true ? "ON" : "OFF" }),
            CapabilityType.Dimmer => CoerceToNumber(value) is { } b ? JsonSerializer.Serialize(new { brightness = (int)(b / 100.0 * 254.0) }) : null,
            CapabilityType.Light => BuildLightPayload(value),
            CapabilityType.Cover => CoerceToNumber(value) is { } p ? JsonSerializer.Serialize(new { position = (int)p }) : null,
            CapabilityType.Update => BuildUpdatePayload(value),
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
