using Akka.Actor;
using Akka.Event;
using MQTTnet;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Bambu;

// Local, add-by-IP integration modelled on ShellyBridgeActor (NOT a cloud-account manifest like
// Dyson). A printer is probed by IP + access code, its serial is read off the local MQTT stream,
// and it enters the standard discovered-devices pipeline. On accept, the per-device settings
// (host, accessCode) arrive via RegisterDeviceForPolling.Settings and one child actor is spawned.
public sealed class BambuBridgeActor : PluginActorBase
{
    protected override string PluginId => "bambu";
    public static Akka.Actor.Props Props() => Akka.Actor.Props.Create(() => new BambuBridgeActor());

    private readonly Dictionary<string, IActorRef> _children = new();    // serial -> child
    private readonly Dictionary<IActorRef, string> _childSerials = new();
    private readonly Dictionary<string, (BambuConfig Cfg, Guid DeviceId)> _pending = new();

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private sealed record ProbeResult(
        string Host, string AccessCode, string? Serial, string? Model, string? ReportJson, string? Error);

    public BambuBridgeActor()
    {
        Receive<DeviceCommand>(cmd =>
        {
            if (_children.TryGetValue(cmd.NativeId, out var child)) child.Forward(cmd);
            else Log.Warning("No Bambu child for {NativeId}", cmd.NativeId);
        });

        Receive<CaptureSnapshot>(msg =>
        {
            if (_children.TryGetValue(msg.NativeId, out var child)) child.Forward(msg); // preserve ask sender
            else Sender.Tell(new SnapshotResult(null));
        });

        // Add-by-IP discovery: probe the printer off the actor thread, deliver the result as a message.
        Receive<DiscoverBambuDevice>(msg =>
        {
            Log.Info("Probing Bambu printer at {Host}", msg.Host);
            ProbeAsync(msg.Host, msg.AccessCode).PipeTo(Self,
                failure: ex => new ProbeResult(msg.Host, msg.AccessCode, null, null, null, ex.Message));
        });

        Receive<ProbeResult>(r =>
        {
            if (r.Serial is null || r.ReportJson is null)
            {
                Log.Warning("Bambu probe of {Host} failed: {Error}", r.Host, r.Error ?? "no report");
                return;
            }

            var deviceId = GetDeviceId(r.Serial) ?? Guid.NewGuid();
            Discover(deviceId, r.Serial, BambuStateMapper.BuildCapabilities(r.ReportJson), new Dictionary<string, string>
            {
                ["serial"] = r.Serial,
                ["host"] = r.Host,
                ["accessCode"] = r.AccessCode,
                ["model"] = r.Model ?? "",
                ["name"] = string.IsNullOrEmpty(r.Model) ? r.Serial : $"{r.Model} ({r.Serial})",
            });
            Log.Info("Discovered Bambu printer {Serial} at {Host}", r.Serial, r.Host);
        });

        Receive<Terminated>(t =>
        {
            if (!_childSerials.TryGetValue(t.ActorRef, out var serial)) return;
            _childSerials.Remove(t.ActorRef);
            if (_pending.TryGetValue(serial, out var p)) { _pending.Remove(serial); SpawnChild(serial, p.Cfg, p.DeviceId); }
            else if (_children.TryGetValue(serial, out var cur) && cur.Equals(t.ActorRef)) _children.Remove(serial);
        });
    }

    // ── PluginActorBase overrides ───────────────────────────────────────────

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        // Children are spawned by OnDeviceRegistered, which the base invokes for each registration.
        PublishStatus("running", _children.Count);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        // A Bambu "config" is per-device (via discovery), not a plugin-wide broker setting, so there
        // is nothing to re-read here — just report status. Enable/disable is a no-op for now.
        PublishStatus(enabled ? "running" : "stopped", _children.Count);
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        SpawnOrRestart(nativeId, deviceId, ConfigFromRegistration(registration));
        PublishStatus("running", _children.Count);
    }

    // ── Spawn / restart ─────────────────────────────────────────────────────

    private void SpawnOrRestart(string serial, Guid deviceId, BambuConfig cfg)
    {
        if (_pending.ContainsKey(serial)) { _pending[serial] = (cfg, deviceId); return; }
        if (_children.TryGetValue(serial, out var existing))
        {
            _pending[serial] = (cfg, deviceId);
            _children.Remove(serial);
            Context.Watch(existing);
            Context.Stop(existing);
            return;
        }
        SpawnChild(serial, cfg, deviceId);
    }

    private void SpawnChild(string serial, BambuConfig cfg, Guid deviceId)
    {
        var child = Context.ActorOf(BambuDeviceActor.Props(cfg, deviceId), $"bambu-device-{serial}");
        _children[serial] = child;
        _childSerials[child] = serial;
    }

    private static BambuConfig ConfigFromRegistration(RegisterDeviceForPolling reg)
    {
        var s = reg.Settings ?? new Dictionary<string, string>();
        return new BambuConfig(
            s.GetValueOrDefault("host", reg.Host),
            reg.NativeId, // serial
            s.GetValueOrDefault("accessCode", ""),
            s.GetValueOrDefault("model", ""),
            s.GetValueOrDefault("name", reg.NativeId));
    }

    // ── Probe (static: no actor state) ──────────────────────────────────────

    // Connects to the printer's local MQTT, subscribes to the wildcard report topic, and returns the
    // serial (from the topic "device/{serial}/report") plus the first report payload — without needing
    // to know the serial in advance. Bambu printers publish reports periodically in LAN mode.
    private static async Task<ProbeResult> ProbeAsync(string host, string accessCode)
    {
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        var tcs = new TaskCompletionSource<(string Serial, string Report)>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.ApplicationMessageReceivedAsync += e =>
        {
            var parts = e.ApplicationMessage.Topic.Split('/');
            if (parts.Length >= 3 && parts[0] == "device" && parts[2] == "report" && parts[1].Length > 0)
                tcs.TrySetResult((parts[1], e.ApplicationMessage.ConvertPayloadToString() ?? "{}"));
            return Task.CompletedTask;
        };

        try
        {
            var connect = await client.ConnectAsync(BambuMqttOptions.Build(host, 8883, accessCode));
            if (connect.ResultCode != MqttClientConnectResultCode.Success)
                return new ProbeResult(host, accessCode, null, null, null, $"connect failed: {connect.ResultCode}");

            await client.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic("device/+/report").WithAtMostOnceQoS().Build());

            var done = await Task.WhenAny(tcs.Task, Task.Delay(ProbeTimeout));
            if (done != tcs.Task)
                return new ProbeResult(host, accessCode, null, null, null,
                    "no report received (is the printer in LAN Mode and the access code correct?)");

            var (serial, report) = await tcs.Task;
            return new ProbeResult(host, accessCode, serial, null, report, null);
        }
        finally
        {
            try { await client.DisconnectAsync(); } catch { /* best effort */ }
        }
    }
}
