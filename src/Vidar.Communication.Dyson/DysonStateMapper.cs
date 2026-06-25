using System.Text.Json;

namespace Vidar.Communication.Dyson;

public static class DysonStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapState(string payload)
    {
        var result = new List<(string, object)>();
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("msg", out var msgEl)) return result;
        var msg = msgEl.GetString();

        if (msg is "CURRENT-STATE" or "STATE-CHANGE")
        {
            if (!root.TryGetProperty("product-state", out var ps)) return result;
            AddBool(result, ps, "fpwr", "power");
            AddInt(result, ps, "fnsp", "fan_speed");          // 0001..0010 -> 1..10; "AUTO" -> skipped numeric
            AddBool(result, ps, "auto", "auto");
            AddBool(result, ps, "nmod", "night_mode");
            AddBool(result, ps, "oson", "oscillation");
            AddBool(result, ps, "rhtm", "continuous_monitoring");
            AddInt(result, ps, "hflr", "hepa_filter");        // percent
            AddInt(result, ps, "cflr", "carbon_filter");      // percent
        }
        else if (msg is "ENVIRONMENTAL-CURRENT-SENSOR-DATA")
        {
            if (!root.TryGetProperty("data", out var data)) return result;
            AddInt(result, data, "pm25", "pm25");
            AddInt(result, data, "pm10", "pm10");
            AddInt(result, data, "va10", "voc");
            AddInt(result, data, "noxl", "no2");
            AddInt(result, data, "hact", "humidity");
            if (TryReadString(data, "tact", out var tact) && double.TryParse(tact, out var deciK))
                result.Add(("temperature", Math.Round(deciK - 2731.5, 0) / 10.0));
        }

        return result;
    }

    private static bool TryReadString(JsonElement parent, string field, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(field, out var el)) return false;
        // STATE-CHANGE encodes ["old","new"]; take new (last). Otherwise plain string.
        if (el.ValueKind == JsonValueKind.Array)
        {
            var arr = el.EnumerateArray().ToArray();
            if (arr.Length == 0) return false;
            value = arr[^1].GetString() ?? "";
            return true;
        }
        if (el.ValueKind == JsonValueKind.String) { value = el.GetString() ?? ""; return true; }
        return false;
    }

    private static void AddBool(List<(string, object)> result, JsonElement parent, string field, string key)
    {
        if (TryReadString(parent, field, out var s) && (s == "ON" || s == "OFF"))
            result.Add((key, s == "ON"));
    }

    private static void AddInt(List<(string, object)> result, JsonElement parent, string field, string key)
    {
        if (TryReadString(parent, field, out var s) && int.TryParse(s, out var n))
            result.Add((key, (double)n));
    }
}
