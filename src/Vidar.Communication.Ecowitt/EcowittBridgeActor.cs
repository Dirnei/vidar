using System.Security;
using Akka.Actor;
using Akka.Event;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Ecowitt;

/// <summary>
/// Subscribes to the Ecowitt gateway's native MQTT publications on EMQX, parses
/// each payload, and reports one weather-station device to the twin. Read-only:
/// no commands, no child actors. Traffic is ~1 message/interval, so payloads are
/// handled directly on the actor thread (no streams needed).
/// </summary>
public sealed class EcowittBridgeActor : PluginActorBase
{
    protected override string PluginId => "ecowitt";

    private readonly EcowittConfig _defaultConfig;
    private EcowittConfig _config;

    private HiveMQClient? _mqttClient;
    private Guid _deviceId;
    private string? _nativeId;
    private bool _discovered;
    private DateTime _lastMessageUtc = DateTime.MinValue;

    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class CheckConnection { public static readonly CheckConnection Instance = new(); }
    private sealed record PayloadReceived(string Payload);

    public static Props Props(EcowittConfig config) =>
        Akka.Actor.Props.Create(() => new EcowittBridgeActor(config));

    public EcowittBridgeActor(EcowittConfig config)
    {
        _defaultConfig = config;
        _config = config;

        ReceiveAsync<ConnectToBroker>(_ => ConnectAsync());
        Receive<CheckConnection>(_ => OnCheckConnection());
        Receive<PayloadReceived>(msg => OnPayload(msg.Payload));
    }

    protected override void PreStart()
    {
        base.PreStart();
        Timers.StartPeriodicTimer("health", CheckConnection.Instance,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    protected override void PostStop()
    {
        _mqttClient?.DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        base.PostStop();
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<Vidar.Core.Messages.RegisterDeviceForPolling> registrations)
    {
        if (enabled)
        {
            ApplySettings(settings);
            Self.Tell(ConnectToBroker.Instance);
        }
        else
        {
            PublishStatus("stopped", _discovered ? 1 : 0);
        }
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (!enabled)
        {
            _ = DisconnectAsync();
            PublishStatus("stopped", _discovered ? 1 : 0);
            return;
        }

        ApplySettings(settings);
        Self.Tell(ConnectToBroker.Instance);
    }

    private void ApplySettings(Dictionary<string, string> s)
    {
        _config = new EcowittConfig(
            MqttHost: s.GetValueOrDefault("mqttHost") ?? _defaultConfig.MqttHost,
            MqttPort: int.TryParse(s.GetValueOrDefault("mqttPort"), out var p) ? p : _defaultConfig.MqttPort,
            MqttUser: s.GetValueOrDefault("mqttUser") ?? _defaultConfig.MqttUser,
            MqttPassword: s.GetValueOrDefault("mqttPassword") ?? _defaultConfig.MqttPassword,
            Topic: s.GetValueOrDefault("topic") ?? _defaultConfig.Topic,
            StaleAfterSeconds: int.TryParse(s.GetValueOrDefault("staleAfterSeconds"), out var st)
                ? st : _defaultConfig.StaleAfterSeconds);
    }

    private async Task ConnectAsync()
    {
        try
        {
            if (_mqttClient != null)
            {
                try { await _mqttClient.DisconnectAsync(); } catch { /* ignore */ }
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            var options = new HiveMQClientOptionsBuilder()
                .WithBroker(_config.MqttHost)
                .WithPort(_config.MqttPort)
                .WithClientId($"vidar-ecowitt-{Guid.NewGuid():N}");

            if (!string.IsNullOrEmpty(_config.MqttUser))
                options.WithUserName(_config.MqttUser);
            if (!string.IsNullOrEmpty(_config.MqttPassword))
            {
                var secure = new SecureString();
                foreach (var c in _config.MqttPassword) secure.AppendChar(c);
                secure.MakeReadOnly();
                options.WithPassword(secure);
            }

            _mqttClient = new HiveMQClient(options.Build());

            var self = Self;
            _mqttClient.OnMessageReceived += (_, e) =>
            {
                var payload = e.PublishMessage.PayloadAsString;
                if (!string.IsNullOrEmpty(payload))
                    self.Tell(new PayloadReceived(payload));
            };
            _mqttClient.OnDisconnectReceived += (_, _) =>
            {
                Log.Warning("Ecowitt MQTT disconnected");
            };

            var result = await _mqttClient.ConnectAsync();
            Log.Info("Ecowitt MQTT connect result: {Result}", result.ReasonCode);

            // The gateway may publish to the exact topic or a subtree — subscribe to both.
            await _mqttClient.SubscribeAsync(_config.Topic, QualityOfService.AtLeastOnceDelivery);
            await _mqttClient.SubscribeAsync($"{_config.Topic}/#", QualityOfService.AtLeastOnceDelivery);

            Log.Info("Connected to {Host}:{Port}, subscribed to {Topic}(/#)",
                _config.MqttHost, _config.MqttPort, _config.Topic);
            PublishStatus("running", _discovered ? 1 : 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to MQTT broker at {Host}:{Port}",
                _config.MqttHost, _config.MqttPort);
            PublishStatus("error", _discovered ? 1 : 0, ex.Message);
        }
    }

    private async Task DisconnectAsync()
    {
        if (_mqttClient != null)
        {
            try { await _mqttClient.DisconnectAsync(); } catch { /* ignore */ }
            _mqttClient.Dispose();
            _mqttClient = null;
        }
    }

    private void OnPayload(string payload)
    {
        var fields = EcowittStateMapper.ParsePayload(payload);
        var passkey = EcowittStateMapper.TryGetPassKey(fields);
        if (passkey == null)
        {
            Log.Warning("Ecowitt payload missing PASSKEY — ignoring");
            return;
        }

        _lastMessageUtc = DateTime.UtcNow;

        if (!_discovered || _nativeId != passkey)
        {
            _nativeId = passkey;
            var configured = GetDeviceId(passkey);
            _deviceId = configured ?? (_deviceId == Guid.Empty ? Guid.NewGuid() : _deviceId);

            Discover(_deviceId, passkey,
                EcowittStateMapper.BuildCapabilities(fields),
                EcowittStateMapper.BuildMetadata(fields));
            _discovered = true;
            PublishStatus("running", 1);
            Log.Info("Discovered Ecowitt station {PassKey}", passkey);
        }

        foreach (var update in EcowittStateMapper.Map(_deviceId, fields))
            ReportState(update.DeviceId, update.CapabilityKey, update.Value);
    }

    private void OnCheckConnection()
    {
        if (!IsEnabled)
            return;

        if (_mqttClient == null || !_mqttClient.IsConnected())
        {
            Log.Warning("Ecowitt MQTT not connected, reconnecting...");
            Self.Tell(ConnectToBroker.Instance);
            return;
        }

        if (_discovered && _config.StaleAfterSeconds > 0 &&
            (DateTime.UtcNow - _lastMessageUtc).TotalSeconds > _config.StaleAfterSeconds)
        {
            PublishStatus("degraded", 1, $"No data received for over {_config.StaleAfterSeconds}s");
        }
    }
}
