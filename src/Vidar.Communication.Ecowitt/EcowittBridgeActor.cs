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

    // Null until the user supplies at least an MQTT host + topic via the Applications UI.
    // While null the actor stays idle and never touches the broker.
    private EcowittConfig? _config;

    private HiveMQClient? _mqttClient;
    private Guid _deviceId;
    private string? _nativeId;
    private bool _discovered;
    private DateTime _lastMessageUtc = DateTime.MinValue;

    private sealed class ConnectToBroker { public static readonly ConnectToBroker Instance = new(); }
    private sealed class CheckConnection { public static readonly CheckConnection Instance = new(); }
    // Internal (not private) so tests can drive the payload path without a live broker.
    internal sealed record PayloadReceived(string Payload);

    public static Props Props() =>
        Akka.Actor.Props.Create(() => new EcowittBridgeActor());

    public EcowittBridgeActor()
    {
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
        if (enabled && ApplySettings(settings))
        {
            Self.Tell(ConnectToBroker.Instance);
        }
        else
        {
            // Disabled, or enabled but not yet configured — stay idle, no broker contact.
            PublishStatus(enabled ? "unconfigured" : "stopped", _discovered ? 1 : 0);
        }
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (enabled && ApplySettings(settings))
        {
            Self.Tell(ConnectToBroker.Instance);
            return;
        }

        _ = DisconnectAsync();
        PublishStatus(enabled ? "unconfigured" : "stopped", _discovered ? 1 : 0);
    }

    /// <summary>
    /// Builds the runtime config from UI settings. Returns false (and clears the config)
    /// when the required connection details — MQTT host and topic — are missing, so the
    /// caller leaves the actor idle rather than dialing a guessed broker.
    /// </summary>
    private bool ApplySettings(Dictionary<string, string> s)
    {
        var host = s.GetValueOrDefault("mqttHost");
        var topic = s.GetValueOrDefault("topic");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(topic))
        {
            _config = null;
            return false;
        }

        _config = new EcowittConfig(
            MqttHost: host,
            MqttPort: int.TryParse(s.GetValueOrDefault("mqttPort"), out var p) ? p : 1883,
            MqttUser: s.GetValueOrDefault("mqttUser"),
            MqttPassword: s.GetValueOrDefault("mqttPassword"),
            Topic: topic,
            StaleAfterSeconds: int.TryParse(s.GetValueOrDefault("staleAfterSeconds"), out var st) ? st : 300);
        return true;
    }

    private async Task ConnectAsync()
    {
        if (_config is not { } config)
            return;

        try
        {
            if (_mqttClient != null)
            {
                try { await _mqttClient.DisconnectAsync(); } catch { /* ignore */ }
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            var options = new HiveMQClientOptionsBuilder()
                .WithBroker(config.MqttHost)
                .WithPort(config.MqttPort)
                .WithClientId($"vidar-ecowitt-{Guid.NewGuid():N}");

            if (!string.IsNullOrEmpty(config.MqttUser))
                options.WithUserName(config.MqttUser);
            if (!string.IsNullOrEmpty(config.MqttPassword))
            {
                var secure = new SecureString();
                foreach (var c in config.MqttPassword) secure.AppendChar(c);
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
            await _mqttClient.SubscribeAsync(config.Topic, QualityOfService.AtLeastOnceDelivery);
            await _mqttClient.SubscribeAsync($"{config.Topic}/#", QualityOfService.AtLeastOnceDelivery);

            Log.Info("Connected to {Host}:{Port}, subscribed to {Topic}(/#)",
                config.MqttHost, config.MqttPort, config.Topic);
            PublishStatus("running", _discovered ? 1 : 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to connect to MQTT broker at {Host}:{Port}",
                config.MqttHost, config.MqttPort);
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

        // Resolve the target device id on EVERY payload. Until the station is adopted,
        // GetDeviceId returns null and we report to a stable synthetic id; once the user
        // adopts it (registering PASSKEY -> the real device id), state must follow to that
        // device. Re-resolving here — rather than only on first discovery — is what lets a
        // late adoption, or a startup race where the registration lands after the first
        // payload, redirect state onto the adopted device instead of stranding it on the
        // synthetic id.
        var configured = GetDeviceId(passkey);
        var targetId = configured ?? (_deviceId == Guid.Empty ? Guid.NewGuid() : _deviceId);

        if (!_discovered || _nativeId != passkey || targetId != _deviceId)
        {
            _nativeId = passkey;
            _deviceId = targetId;

            Discover(_deviceId, passkey,
                EcowittStateMapper.BuildCapabilities(fields),
                EcowittStateMapper.BuildMetadata(fields));
            _discovered = true;
            PublishStatus("running", 1);
            Log.Info("Reporting Ecowitt station {PassKey} as device {DeviceId}", passkey, _deviceId);
        }

        foreach (var update in EcowittStateMapper.Map(_deviceId, fields))
            ReportState(update.DeviceId, update.CapabilityKey, update.Value);
    }

    private void OnCheckConnection()
    {
        // Nothing to watch while disabled or not yet configured.
        if (!IsEnabled || _config is not { } config)
            return;

        if (_mqttClient == null || !_mqttClient.IsConnected())
        {
            Log.Warning("Ecowitt MQTT not connected, reconnecting...");
            Self.Tell(ConnectToBroker.Instance);
            return;
        }

        if (_discovered && config.StaleAfterSeconds > 0 &&
            (DateTime.UtcNow - _lastMessageUtc).TotalSeconds > config.StaleAfterSeconds)
        {
            PublishStatus("degraded", 1, $"No data received for over {config.StaleAfterSeconds}s");
        }
    }
}
