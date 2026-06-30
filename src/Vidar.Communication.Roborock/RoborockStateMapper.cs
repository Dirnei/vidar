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

        if (root.TryGetProperty("fan_power", out var fp) && fp.ValueKind == JsonValueKind.Number)
            result.Add(("vacuum.fanPower", fp.GetInt32()));

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
}
