using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using HiveMQtt.Client;
using HiveMQtt.Client.Events;
using HiveMQtt.MQTT5.Types;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.Zigbee2Mqtt;

public sealed class Zigbee2MqttBridgeActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _mqttHost;
    private readonly int _mqttPort;
    private readonly IActorRef _shardProxy;
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByFriendlyName = new();
    private readonly Dictionary<string, Zigbee2MqttDevice> _devicesByIeeeAddress = new();
    private HiveMQClient? _mqttClient;

    private sealed record MqttMessageReceived(string Topic, string Payload);
    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }

    public static Props Props(string mqttHost, int mqttPort, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new Zigbee2MqttBridgeActor(mqttHost, mqttPort, shardProxy));

    public Zigbee2MqttBridgeActor(string mqttHost, int mqttPort, IActorRef shardProxy)
    {
        _mqttHost = mqttHost;
        _mqttPort = mqttPort;
        _shardProxy = shardProxy;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());

        Receive<MqttMessageReceived>(msg => HandleMqttMessage(msg.Topic, msg.Payload));

        ReceiveAsync<DeviceCommand>(async cmd =>
        {
            var device = _devicesByFriendlyName.Values.FirstOrDefault(d => d.VidarDeviceId == cmd.DeviceId)
                         ?? _devicesByIeeeAddress.Values.FirstOrDefault(d => d.VidarDeviceId == cmd.DeviceId);
            if (device == null || _mqttClient == null) return;

            var payload = BuildCommandPayload(cmd.Capability, cmd.Value);
            if (payload != null)
            {
                var topic = $"zigbee2mqtt/{device.FriendlyName}/set";
                await _mqttClient.PublishAsync(topic, payload, QualityOfService.AtMostOnceDelivery);
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("commands.zigbee2mqtt", Self));
        Self.Tell(ConnectToBroker.Instance);
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
            var options = new HiveMQClientOptionsBuilder()
                .WithBroker(_mqttHost)
                .WithPort(_mqttPort)
                .WithClientId($"vidar-zigbee2mqtt-{Guid.NewGuid():N}")
                .Build();

            _mqttClient = new HiveMQClient(options);
            _mqttClient.OnMessageReceived += OnMqttMessageReceived;

            await _mqttClient.ConnectAsync();
            await _mqttClient.SubscribeAsync("zigbee2mqtt/#", QualityOfService.AtMostOnceDelivery);

            _log.Info("Connected to MQTT broker at {Host}:{Port}", _mqttHost, _mqttPort);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to connect to MQTT broker at {Host}:{Port}", _mqttHost, _mqttPort);
        }
    }

    private void OnMqttMessageReceived(object? sender, OnMessageReceivedEventArgs e)
    {
        var topic = e.PublishMessage.Topic;
        var payload = e.PublishMessage.PayloadAsString;
        if (topic != null && payload != null)
            Self.Tell(new MqttMessageReceived(topic, payload));
    }

    private void HandleMqttMessage(string topic, string payload)
    {
        if (topic == "zigbee2mqtt/bridge/devices")
        {
            HandleDeviceList(payload);
            return;
        }

        if (topic.StartsWith("zigbee2mqtt/") && !topic.Contains("/set") && !topic.Contains("/get") && !topic.StartsWith("zigbee2mqtt/bridge/"))
        {
            var friendlyName = topic["zigbee2mqtt/".Length..];
            if (_devicesByFriendlyName.TryGetValue(friendlyName, out var device) && device.VidarDeviceId != null)
            {
                var updates = Zigbee2MqttStateMapper.MapState(payload, device.Capabilities);
                foreach (var u in updates)
                    _shardProxy.Tell(new DeviceStateUpdate(device.VidarDeviceId.Value, u.Capability, u.Value));
            }
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

                var capabilities = new List<CapabilityType>();
                if (deviceEl.TryGetProperty("definition", out var definition) &&
                    definition.TryGetProperty("exposes", out var exposes))
                {
                    capabilities = ExposesMapper.MapCapabilities(exposes);
                }

                var metadata = new Dictionary<string, string>();
                if (deviceEl.TryGetProperty("manufacturer", out var mfr) && mfr.GetString() != null)
                    metadata["manufacturer"] = mfr.GetString()!;
                if (deviceEl.TryGetProperty("model_id", out var modelId) && modelId.GetString() != null)
                    metadata["model_id"] = modelId.GetString()!;

                if (_devicesByIeeeAddress.TryGetValue(ieeeAddress, out var existing))
                {
                    existing.Capabilities.Clear();
                    existing.Capabilities.AddRange(capabilities);
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

                    _log.Info("Discovered Zigbee2MQTT device: {FriendlyName} ({IeeeAddress})", friendlyName, ieeeAddress);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to parse Zigbee2MQTT device list");
        }
    }

    private static string? BuildCommandPayload(CapabilityType capability, object value)
    {
        return capability switch
        {
            CapabilityType.Switch when value is bool on =>
                JsonSerializer.Serialize(new { state = on ? "ON" : "OFF" }),
            CapabilityType.Dimmer when value is int brightness =>
                JsonSerializer.Serialize(new { brightness }),
            CapabilityType.Dimmer when value is double brightnessDbl =>
                JsonSerializer.Serialize(new { brightness = (int)brightnessDbl }),
            CapabilityType.Cover when value is int pos =>
                JsonSerializer.Serialize(new { position = pos }),
            CapabilityType.Temperature => null,
            _ => null
        };
    }
}
