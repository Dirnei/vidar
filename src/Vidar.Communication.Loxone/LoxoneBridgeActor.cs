using System.Text.Json;
using Akka.Actor;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Loxone;

public sealed class LoxoneBridgeActor : PluginActorBase
{
    protected override string PluginId => "loxone";

    public static Props Props(string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new LoxoneBridgeActor(brokerHost, brokerPort, baseTopic));

    private readonly string _brokerHost;
    private readonly int _brokerPort;
    private readonly string _baseTopic;

    // serial -> per-Miniserver child (owns that serial's MQTT connection)
    private readonly Dictionary<string, IActorRef> _children = new();

    public LoxoneBridgeActor(string brokerHost, int brokerPort, string baseTopic)
    {
        _brokerHost = brokerHost;
        _brokerPort = brokerPort;
        _baseTopic = baseTopic;

        // The child parsed a fresh structure manifest — discover every control. NativeId is
        // "<serial>/<uuid>". Re-runs on every structure republish (structure re-sync): the host
        // DiscoveryManager absorbs capability changes; the child publishes a manifest snapshot
        // (Task 7) so vanished controls get retired.
        Receive<ControlsDiscovered>(msg =>
        {
            var byUuid = new Dictionary<string, Guid>();

            foreach (var control in msg.Structure.Controls)
            {
                var capabilities = LoxoneControlMapper.Map(control);
                if (capabilities.Count == 0) continue; // present-fields: skip unsupported types

                var nativeId = $"{msg.Serial}/{control.Uuid}";
                var deviceId = GetDeviceId(nativeId) ?? Guid.NewGuid();
                byUuid[control.Uuid] = deviceId;
                var roomName = msg.Structure.Rooms.FirstOrDefault(r => r.Uuid == control.RoomUuid)?.Name ?? "";

                Discover(deviceId, nativeId, capabilities, new Dictionary<string, string>
                {
                    ["serial"] = msg.Serial,
                    ["uuid"] = control.Uuid,
                    ["type"] = control.Type,
                    ["name"] = control.Name,
                    ["loxoneRoomUuid"] = control.RoomUuid ?? "",
                    ["loxoneRoomName"] = roomName,
                });
            }

            // Reply with the uuid->deviceId map so the child can tag DeviceStateUpdates and route
            // commands without re-deriving id assignment itself (the bridge owns GetDeviceId/Discover).
            Sender.Tell(new ControlIds(msg.Serial, byUuid));

            PublishStatus("running", _children.Count);
        });

        // Route a command to the child owning its serial (NativeId = "<serial>/<uuid>").
        Receive<DeviceCommand>(cmd =>
        {
            var serial = cmd.NativeId.Split('/', 2)[0];
            if (_children.TryGetValue(serial, out var child)) child.Forward(cmd);
            else Log.Warning("No Loxone child for serial {Serial} (NativeId {NativeId})", serial, cmd.NativeId);
        });
    }

    protected override void OnPluginRegistered(bool enabled,
        Dictionary<string, string> settings, List<RegisterDeviceForPolling> registrations)
    {
        SyncChildren(enabled, settings);
        PublishStatus(enabled ? "running" : "stopped", _children.Count);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        SyncChildren(enabled, settings);
        PublishStatus(enabled ? "running" : "stopped", _children.Count);
    }

    // A device registration arrived (bulk on plugin (re)register, or a single freshly-accepted
    // device). The owning child may have already run discovery before this id was known — retained
    // MQTT structure races the cluster registration round-trip — and tagged the control with an
    // ephemeral id, sending its state to a nonexistent twin. Push the resolved id so the child
    // re-tags the control and flushes its last state. NativeId is "<serial>/<uuid>".
    protected override void OnDeviceRegistered(Guid deviceId, string nativeId,
        RegisterDeviceForPolling registration)
    {
        var parts = nativeId.Split('/', 2);
        if (parts.Length == 2 && _children.TryGetValue(parts[0], out var child))
            child.Tell(new ResolveControlId(parts[1], deviceId));
    }

    // Spawn a child per configured Miniserver; stop children whose Miniserver was removed.
    private void SyncChildren(bool enabled, Dictionary<string, string> settings)
    {
        var wanted = enabled
            ? ParseManifest(settings).ToDictionary(m => m.Serial)
            : new Dictionary<string, LoxoneMiniserver>();

        foreach (var (serial, child) in _children.ToList())
        {
            if (!wanted.ContainsKey(serial))
            {
                Context.Stop(child);
                _children.Remove(serial);
            }
        }

        foreach (var (serial, ms) in wanted)
        {
            if (_children.ContainsKey(serial)) continue;
            var child = Context.ActorOf(
                LoxoneMiniserverActor.Props(ms, _brokerHost, _brokerPort, _baseTopic),
                $"loxone-ms-{serial}");
            _children[serial] = child;
        }
    }

    public static List<LoxoneMiniserver> ParseManifest(IReadOnlyDictionary<string, string> settings)
    {
        var result = new List<LoxoneMiniserver>();
        if (!settings.TryGetValue("miniservers", out var json) || string.IsNullOrWhiteSpace(json))
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
                var serial = Str(e, "serial");
                var host = Str(e, "host");
                if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(host)) continue;
                result.Add(new LoxoneMiniserver(serial!, host!, Str(e, "user") ?? "", Str(e, "password") ?? ""));
            }
        }
        return result;

        static string? Str(JsonElement o, string p) =>
            o.TryGetProperty(p, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }
}
