using System.Text.Json;
using Akka.Actor;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Bambu;

public sealed class BambuBridgeActor : PluginActorBase
{
    protected override string PluginId => "bambu";
    public static Akka.Actor.Props Props() => Akka.Actor.Props.Create(() => new BambuBridgeActor());

    private readonly Dictionary<string, BambuConfig> _manifest = new();  // serial -> config
    private readonly Dictionary<string, IActorRef> _children = new();    // serial -> child
    private readonly Dictionary<IActorRef, string> _childSerials = new();
    private readonly Dictionary<string, (BambuConfig Cfg, Guid DeviceId)> _pending = new();

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

        Receive<Terminated>(t =>
        {
            if (!_childSerials.TryGetValue(t.ActorRef, out var serial)) return;
            _childSerials.Remove(t.ActorRef);
            if (_pending.TryGetValue(serial, out var p)) { _pending.Remove(serial); SpawnChild(serial, p.Cfg, p.DeviceId); }
            else if (_children.TryGetValue(serial, out var cur) && cur.Equals(t.ActorRef)) _children.Remove(serial);
        });
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        RefreshManifest(settings);
        foreach (var reg in registrations) SpawnOrRestart(reg.NativeId, reg.DeviceId);
        if (enabled) DiscoverAll();
        PublishStatus("running", _children.Count);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (!enabled) { PublishStatus("stopped", 0); return; }
        RefreshManifest(settings);
        DiscoverAll();
        PublishStatus("running", _children.Count);
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        if (_manifest.Count == 0) return;
        SpawnOrRestart(nativeId, deviceId);
        PublishStatus("running", _children.Count);
    }

    private void RefreshManifest(Dictionary<string, string> settings)
    {
        _manifest.Clear();
        foreach (var c in ParseManifest(settings)) _manifest[c.Serial] = c;
    }

    private void DiscoverAll()
    {
        foreach (var (serial, cfg) in _manifest)
        {
            var deviceId = GetDeviceId(serial) ?? Guid.NewGuid();
            Discover(deviceId, serial, DefaultCapabilities(), new Dictionary<string, string>
            {
                ["serial"] = serial, ["model"] = cfg.Model, ["name"] = cfg.Name, ["host"] = cfg.Host,
            });
        }
    }

    private void SpawnOrRestart(string serial, Guid deviceId)
    {
        if (!_manifest.TryGetValue(serial, out var cfg)) return;
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

    public static List<BambuConfig> ParseManifest(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue("account.manifest", out var json) || string.IsNullOrWhiteSpace(json))
            return new();
        using var doc = JsonDocument.Parse(json);
        var list = new List<BambuConfig>();
        foreach (var e in doc.RootElement.EnumerateArray())
            list.Add(new BambuConfig(
                e.GetProperty("host").GetString()!,
                e.GetProperty("serial").GetString()!,
                e.GetProperty("accessCode").GetString()!,
                e.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));
        return list;
    }

    // Capabilities before the first report: fixed commands + the common sensors present on all models.
    private static List<CapabilityDescriptor> DefaultCapabilities() =>
        BambuStateMapper.BuildCapabilities("""
        { "print": { "gcode_state":"IDLE","mc_percent":0,"mc_remaining_time":0,"layer_num":0,"total_layer_num":0,
          "nozzle_temper":0,"bed_temper":0,"cooling_fan_speed":"0","spd_lvl":2,"subtask_name":"","wifi_signal":"",
          "nozzle_diameter":"0.4","hms":[],"lights_report":[{"node":"chamber_light","mode":"off"}] } }
        """);
}
