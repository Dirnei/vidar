using System.Text.Json;
using Akka.Actor;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Dyson;

public sealed class DysonBridgeActor : PluginActorBase
{
    protected override string PluginId => "dyson";

    public static Akka.Actor.Props Props() =>
        Akka.Actor.Props.Create(() => new DysonBridgeActor());

    // Manifest cache: serial → (credential with Ip=null, display name)
    private readonly Dictionary<string, (DysonDeviceCredential Cred, string Name)> _manifest = new();

    // Live children: serial → actor ref (credential includes the resolved Ip)
    private readonly Dictionary<string, IActorRef> _children = new();

    public DysonBridgeActor()
    {
        Receive<DeviceCommand>(cmd =>
        {
            if (_children.TryGetValue(cmd.NativeId, out var child))
                child.Forward(cmd);
            else
                Log.Warning("No Dyson child actor for NativeId {NativeId}", cmd.NativeId);
        });
    }

    // ── PluginActorBase overrides ───────────────────────────────────────────

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        RefreshManifest(settings);

        // Restore accepted devices that were persisted before this actor started
        foreach (var reg in registrations)
            SpawnOrRestartChild(reg.NativeId, reg.DeviceId, reg.Host);

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
        SpawnOrRestartChild(nativeId, deviceId, registration.Host);
        PublishStatus("running", _children.Count);
    }

    // ── Manifest / discovery helpers ────────────────────────────────────────

    private void RefreshManifest(Dictionary<string, string> settings)
    {
        _manifest.Clear();
        foreach (var (cred, name) in ParseManifestInternal(settings))
            _manifest[cred.Serial] = (cred, name);
    }

    private void DiscoverAll()
    {
        foreach (var (serial, (cred, name)) in _manifest)
        {
            var deviceId = GetDeviceId(serial) ?? Guid.NewGuid();
            var capabilities = BuildCapabilities(cred.ProductType);

            Discover(deviceId, serial, capabilities, new Dictionary<string, string>
            {
                ["serial"] = serial,
                ["productType"] = cred.ProductType,
                ["mqttPassword"] = cred.MqttPassword,
                ["name"] = name,
            });

            Log.Info("Discovered Dyson device {Serial} ({ProductType})", serial, cred.ProductType);
        }
    }

    private void SpawnOrRestartChild(string serial, Guid deviceId, string host)
    {
        if (!_manifest.TryGetValue(serial, out var entry))
        {
            Log.Warning("RegisterDeviceForPolling for unknown serial {Serial} — no manifest entry", serial);
            return;
        }

        var ip = string.IsNullOrWhiteSpace(host) ? null : host;
        var cred = entry.Cred with { Ip = ip };

        // Stop the existing child if any (will be replaced)
        if (_children.TryGetValue(serial, out var existing))
        {
            Context.Stop(existing);
            _children.Remove(serial);
        }

        // Akka actor names must not contain '/' etc.; Dyson serials use letters, digits, dashes — safe
        var childName = $"dyson-device-{serial}";
        var child = Context.ActorOf(DysonDeviceActor.Props(cred, deviceId), childName);
        _children[serial] = child;

        Log.Info("Spawned DysonDeviceActor for {Serial} Ip={Ip}", serial, ip ?? "(none — needs-connection)");
    }

    // ── Static helpers (public ParseManifest for unit tests) ────────────────

    /// <summary>
    /// Parses <c>account.manifest</c> from <paramref name="settings"/>, filters out robots,
    /// and returns one <see cref="DysonDeviceCredential"/> per supported device.
    /// <c>Ip</c> is always <see langword="null"/> here; it arrives later via
    /// <c>RegisterDeviceForPolling.Host</c>.
    /// </summary>
    public static List<DysonDeviceCredential> ParseManifest(IReadOnlyDictionary<string, string> settings)
        => ParseManifestInternal(settings).Select(t => t.Cred).ToList();

    private static List<(DysonDeviceCredential Cred, string Name)> ParseManifestInternal(
        IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue("account.manifest", out var json) || string.IsNullOrWhiteSpace(json))
            return new();

        using var doc = JsonDocument.Parse(json);
        var result = new List<(DysonDeviceCredential, string)>();

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var productType = e.GetProperty("productType").GetString()!;
            if (!DysonModelRegistry.IsSupported(productType))
                continue;

            var serial = e.GetProperty("serial").GetString()!;
            var mqttPassword = e.GetProperty("mqttPassword").GetString()!;
            var name = e.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? serial
                : serial;

            result.Add((new DysonDeviceCredential(serial, productType, mqttPassword, null), name));
        }

        return result;
    }

    private static List<CapabilityDescriptor> BuildCapabilities(string productType) =>
        DysonModelRegistry.Resolve(productType).Capabilities
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
