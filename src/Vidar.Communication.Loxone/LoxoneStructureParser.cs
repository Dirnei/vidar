using System.Text.Json;

namespace Vidar.Communication.Loxone;

public sealed record LoxoneRoom(string Uuid, string Name);

public sealed record LoxoneStructure(
    string Serial,
    IReadOnlyList<LoxoneControl> Controls,
    IReadOnlyList<LoxoneRoom> Rooms);

public static class LoxoneStructureParser
{
    public static LoxoneStructure? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var serial = GetString(root, "serial") ?? "";
            var controls = new List<LoxoneControl>();
            if (root.TryGetProperty("controls", out var ctrls) && ctrls.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in ctrls.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object) continue;
                    var uuid = GetString(c, "uuid");
                    var type = GetString(c, "type");
                    if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(type)) continue;
                    controls.Add(new LoxoneControl(
                        uuid, GetString(c, "name") ?? uuid, type, GetString(c, "room"),
                        ParseMoods(c)));
                }
            }

            var rooms = new List<LoxoneRoom>();
            if (root.TryGetProperty("rooms", out var rms) && rms.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rms.EnumerateArray())
                {
                    var uuid = GetString(r, "uuid");
                    if (string.IsNullOrEmpty(uuid)) continue;
                    rooms.Add(new LoxoneRoom(uuid, GetString(r, "name") ?? uuid));
                }
            }

            return new LoxoneStructure(serial, controls, rooms);
        }
    }

    private static IReadOnlyList<LoxoneMood> ParseMoods(JsonElement control)
    {
        var moods = new List<LoxoneMood>();
        if (!control.TryGetProperty("moods", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return moods;
        foreach (var m in arr.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            if (!m.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number
                || !idEl.TryGetInt32(out var id)) continue;
            moods.Add(new LoxoneMood(id, GetString(m, "name") ?? id.ToString()));
        }
        return moods;
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
