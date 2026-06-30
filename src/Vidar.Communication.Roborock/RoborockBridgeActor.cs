using System.Text.Json;
using Akka.Actor;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Roborock;

public sealed class RoborockBridgeActor : PluginActorBase
{
    protected override string PluginId => "roborock";

    public static Props Props(string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new RoborockBridgeActor(brokerHost, brokerPort, baseTopic));

    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _baseTopic;

    private readonly Dictionary<string, (RoborockDeviceCredential Cred, string Name)> _manifest = new();
    private readonly Dictionary<string, IActorRef> _children = new();
    private readonly Dictionary<IActorRef, string> _childDuids = new();
    private readonly Dictionary<string, (RoborockDeviceCredential Cred, Guid DeviceId)> _pendingRestarts = new();

    public RoborockBridgeActor(string brokerHost, int brokerPort, string baseTopic)
    {
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _baseTopic = baseTopic;

        Receive<DeviceCommand>(cmd =>
        {
            if (_children.TryGetValue(cmd.NativeId, out var child)) child.Forward(cmd);
            else Log.Warning("No Roborock child actor for NativeId {NativeId}", cmd.NativeId);
        });

        Receive<Terminated>(msg =>
        {
            if (!_childDuids.TryGetValue(msg.ActorRef, out var duid)) return;
            _childDuids.Remove(msg.ActorRef);
            if (_pendingRestarts.TryGetValue(duid, out var pending))
            {
                _pendingRestarts.Remove(duid);
                SpawnChild(duid, pending.Cred, pending.DeviceId);
            }
            else if (_children.TryGetValue(duid, out var existing) && existing.Equals(msg.ActorRef))
            {
                _children.Remove(duid);
                Log.Warning("RoborockDeviceActor for {Duid} terminated unexpectedly", duid);
            }
        });
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        RefreshManifest(settings);
        foreach (var reg in registrations) SpawnOrRestartChild(reg.NativeId, reg.DeviceId);
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

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId,
        RegisterDeviceForPolling registration)
    {
        if (_manifest.Count == 0) return;
        SpawnOrRestartChild(nativeId, deviceId);
        PublishStatus("running", _children.Count);
    }

    private void RefreshManifest(Dictionary<string, string> settings)
    {
        _manifest.Clear();
        foreach (var (cred, name) in ParseManifest(settings)) _manifest[cred.Duid] = (cred, name);
    }

    private void DiscoverAll()
    {
        foreach (var (duid, (cred, name)) in _manifest)
        {
            var deviceId = GetDeviceId(duid) ?? Guid.NewGuid();
            Discover(deviceId, duid, RoborockModelRegistry.Capabilities(cred.Model).ToList(),
                new Dictionary<string, string>
                {
                    ["duid"] = duid, ["model"] = cred.Model, ["name"] = name,
                });
            Log.Info("Discovered Roborock device {Duid} ({Model})", duid, cred.Model);
        }
    }

    private void SpawnOrRestartChild(string duid, Guid deviceId)
    {
        if (!_manifest.TryGetValue(duid, out var entry))
        {
            Log.Warning("RegisterDeviceForPolling for unknown duid {Duid}", duid);
            return;
        }
        var cred = entry.Cred;
        if (_pendingRestarts.ContainsKey(duid)) { _pendingRestarts[duid] = (cred, deviceId); return; }
        if (_children.TryGetValue(duid, out var existing))
        {
            _pendingRestarts[duid] = (cred, deviceId);
            _children.Remove(duid);
            Context.Watch(existing);
            Context.Stop(existing);
            return;
        }
        SpawnChild(duid, cred, deviceId);
    }

    private void SpawnChild(string duid, RoborockDeviceCredential cred, Guid deviceId)
    {
        var child = Context.ActorOf(
            RoborockDeviceActor.Props(cred, deviceId, _brokerHost, _brokerPort, _baseTopic),
            $"roborock-device-{duid}");
        _children[duid] = child;
        _childDuids[child] = duid;
        Log.Info("Spawned RoborockDeviceActor for {Duid}", duid);
    }

    // ── Static helpers (public for tests) ───────────────────────────────────
    public static List<(RoborockDeviceCredential Cred, string Name)> ParseManifest(
        IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue("account.manifest", out var json) || string.IsNullOrWhiteSpace(json))
            return new();
        using var doc = JsonDocument.Parse(json);
        var result = new List<(RoborockDeviceCredential, string)>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var model = e.GetProperty("model").GetString() ?? "";
            if (!RoborockModelRegistry.IsSupported(model)) continue;
            var duid = e.GetProperty("duid").GetString()!;
            var name = e.TryGetProperty("name", out var n) ? n.GetString() ?? duid : duid;
            var localKey = e.GetProperty("localKey").GetString() ?? "";
            var ip = e.TryGetProperty("ip", out var ipp) ? ipp.GetString() ?? "" : "";
            result.Add((new RoborockDeviceCredential(duid, model, name, localKey, ip), name));
        }
        return result;
    }

    public static string? AccountUserData(IReadOnlyDictionary<string, string> settings) =>
        settings.TryGetValue("account.userData", out var t) && !string.IsNullOrWhiteSpace(t) ? t : null;
}
