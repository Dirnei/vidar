using System.Text.Json;
using Akka.Actor;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Dreo;

public sealed class DreoBridgeActor : PluginActorBase
{
    protected override string PluginId => "dreo";

    public static Akka.Actor.Props Props(string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new DreoBridgeActor(brokerHost, brokerPort, baseTopic));

    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _baseTopic;

    // Manifest cache: serial → (credential, display name)
    private readonly Dictionary<string, (DreoDeviceCredential Cred, string Name)> _manifest = new();

    // Live children: serial → actor ref
    private readonly Dictionary<string, IActorRef> _children = new();

    // Deferred-restart state: ActorRef → serial (for Terminated lookup)
    private readonly Dictionary<IActorRef, string> _childSerials = new();
    // Pending restarts waiting for old child to terminate: serial → (new cred, deviceId)
    private readonly Dictionary<string, (DreoDeviceCredential Cred, Guid DeviceId)> _pendingRestarts = new();

    public DreoBridgeActor(string brokerHost, int brokerPort, string baseTopic)
    {
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _baseTopic = baseTopic;

        Receive<DeviceCommand>(cmd =>
        {
            if (_children.TryGetValue(cmd.NativeId, out var child))
                child.Forward(cmd);
            else
                Log.Warning("No Dreo child actor for NativeId {NativeId}", cmd.NativeId);
        });

        // Spawn the replacement child only AFTER the old one has fully terminated.
        Receive<Terminated>(msg =>
        {
            if (!_childSerials.TryGetValue(msg.ActorRef, out var serial))
                return;

            _childSerials.Remove(msg.ActorRef);

            if (_pendingRestarts.TryGetValue(serial, out var pending))
            {
                _pendingRestarts.Remove(serial);
                SpawnChild(serial, pending.Cred, pending.DeviceId);
                Log.Info("Replacement DreoDeviceActor spawned for {Serial} after old child terminated", serial);
            }
            else if (_children.TryGetValue(serial, out var existingRef) && existingRef.Equals(msg.ActorRef))
            {
                _children.Remove(serial);
                Log.Warning("DreoDeviceActor for {Serial} terminated unexpectedly (no pending restart)", serial);
            }
        });
    }

    // ── PluginActorBase overrides ───────────────────────────────────────────

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        RefreshManifest(settings);

        // Restore accepted devices that were persisted before this actor started
        foreach (var reg in registrations)
            SpawnOrRestartChild(reg.NativeId, reg.DeviceId);

        if (enabled)
            DiscoverAll();

        PublishStatus("running", _children.Count);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (!enabled)
        {
            PublishStatus("stopped", 0);
            return;
        }

        RefreshManifest(settings);
        DiscoverAll();
        PublishStatus("running", _children.Count);
    }

    // Called by base after it records the device in _configuredDevices
    protected override void OnDeviceRegistered(Guid deviceId, string nativeId,
        RegisterDeviceForPolling registration)
    {
        // During startup, OnPluginRegistered hasn't run RefreshManifest yet, so _manifest is
        // empty. Return silently — the OnPluginRegistered restoration loop will spawn children
        // for all stored registrations once the manifest is populated.
        if (_manifest.Count == 0)
            return;

        SpawnOrRestartChild(nativeId, deviceId);
        PublishStatus("running", _children.Count);
    }

    // ── Manifest / discovery helpers ────────────────────────────────────────

    private void RefreshManifest(Dictionary<string, string> settings)
    {
        _manifest.Clear();
        foreach (var (cred, name) in ParseManifest(settings))
            _manifest[cred.Serial] = (cred, name);
    }

    private void DiscoverAll()
    {
        foreach (var (serial, (cred, name)) in _manifest)
        {
            var deviceId = GetDeviceId(serial) ?? Guid.NewGuid();
            var capabilities = BuildCapabilities(cred.Model);

            Discover(deviceId, serial, capabilities, new Dictionary<string, string>
            {
                ["serial"] = serial,
                ["model"] = cred.Model,
                ["name"] = name,
            });

            Log.Info("Discovered Dreo device {Serial} ({Model})", serial, cred.Model);
        }
    }

    private void SpawnOrRestartChild(string serial, Guid deviceId)
    {
        if (!_manifest.TryGetValue(serial, out var entry))
        {
            Log.Warning("RegisterDeviceForPolling for unknown serial {Serial} — no manifest entry", serial);
            return;
        }

        var cred = entry.Cred;

        // A restart is already in flight for this serial (old child still terminating).
        // Update the pending credential; the Terminated handler will spawn the replacement.
        if (_pendingRestarts.ContainsKey(serial))
        {
            _pendingRestarts[serial] = (cred, deviceId);
            return;
        }

        // If an existing child is running, watch it and stop it, then store the new credential
        // in _pendingRestarts. The Receive<Terminated> handler spawns the replacement only
        // after the old actor's name slot is fully released.
        if (_children.TryGetValue(serial, out var existing))
        {
            _pendingRestarts[serial] = (cred, deviceId);
            _children.Remove(serial);
            Context.Watch(existing);
            Context.Stop(existing);
            Log.Info("Stopping existing DreoDeviceActor for {Serial}; replacement deferred until Terminated", serial);
            return;
        }

        // No existing child — spawn immediately
        SpawnChild(serial, cred, deviceId);
    }

    // Akka actor names must not contain '/' etc.; Dreo serials use letters/digits — safe
    private void SpawnChild(string serial, DreoDeviceCredential cred, Guid deviceId)
    {
        var childName = $"dreo-device-{serial}";
        var child = Context.ActorOf(DreoDeviceActor.Props(cred, deviceId, _brokerHost, _brokerPort, _baseTopic), childName);
        _children[serial] = child;
        _childSerials[child] = serial;

        Log.Info("Spawned DreoDeviceActor for {Serial}", serial);
    }

    // ── Static helpers (public for unit tests) ──────────────────────────────

    /// <summary>
    /// Parses <c>account.manifest</c> into (credential, display name) pairs.
    /// Malformed entries (missing serial) are skipped rather than throwing.
    /// </summary>
    public static List<(DreoDeviceCredential Cred, string Name)> ParseManifest(
        IReadOnlyDictionary<string, string> settings)
    {
        var result = new List<(DreoDeviceCredential, string)>();
        if (!settings.TryGetValue("account.manifest", out var json) || string.IsNullOrWhiteSpace(json))
            return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return result; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (!e.TryGetProperty("serial", out var serialEl) || serialEl.ValueKind != JsonValueKind.String)
                    continue;
                var serial = serialEl.GetString();
                if (string.IsNullOrWhiteSpace(serial)) continue;

                var model = e.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? mEl.GetString() ?? "" : "";
                var name = e.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString() ?? serial : serial;

                result.Add((new DreoDeviceCredential(serial, model), name));
            }
        }
        return result;
    }

    private static List<CapabilityDescriptor> BuildCapabilities(string model) =>
        DreoModelRegistry.Resolve(model).Capabilities
            .Select(c => new CapabilityDescriptor
            {
                Key = c.Key,
                Label = c.Label,
                Unit = Enum.Parse<UnitType>(c.Unit),
                Commandable = c.Commandable,
                Min = c.Min,
                Max = c.Max,
            })
            .ToList();
}
