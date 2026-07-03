using System.Text.Json;

namespace Vidar.Communication.Roborock;

public static class RoborockStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapState(string payload)
    {
        var result = new List<(string, object)>();
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (root.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.Number)
            result.Add(("vacuum.state", MapStateCode(st.GetInt32())));

        if (root.TryGetProperty("battery", out var bat) && bat.ValueKind == JsonValueKind.Number)
            result.Add(("vacuum.battery", bat.GetInt32()));

        // python-roborock's Status.as_dict() camelizes keys, so fan_power -> fanPower.
        if (root.TryGetProperty("fanPower", out var fp) && fp.ValueKind == JsonValueKind.Number)
            result.Add(("vacuum.fanPower", fp.GetInt32()));

        AddList(result, root, "_rooms", "vacuum.rooms");
        AddList(result, root, "_scenes", "vacuum.scenes");

        return result;
    }

    private static string MapStateCode(int code) => code switch
    {
        4 or 5 or 6 or 7 or 11 or 16 or 17 or 18 => "cleaning",
        8 => "docked",
        10 => "paused",
        15 => "returning",
        9 or 12 => "error",
        _ => "idle",
    };

    private static void AddList(List<(string, object)> result, JsonElement root,
        string field, string capabilityKey)
    {
        if (!root.TryGetProperty(field, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;
        var items = new List<Dictionary<string, object>>();
        foreach (var e in arr.EnumerateArray())
        {
            var item = new Dictionary<string, object>();
            if (e.TryGetProperty("id", out var id))
                item["id"] = id.ValueKind == JsonValueKind.Number ? id.GetInt64() : (object?)id.GetString() ?? "";
            if (e.TryGetProperty("name", out var name))
                item["name"] = name.GetString() ?? "";
            items.Add(item);
        }
        if (items.Count > 0)
            result.Add((capabilityKey, items));
    }
}
