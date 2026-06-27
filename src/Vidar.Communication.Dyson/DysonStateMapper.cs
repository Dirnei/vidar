using System.Text.Json;

namespace Vidar.Communication.Dyson;

public static class DysonStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapState(string payload, string productType)
    {
        var result = new List<(string, object)>();
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("msg", out var msgEl)) return result;
        var msg = msgEl.GetString();
        var feature = DysonModelRegistry.Resolve(productType).Feature;

        if (msg is "CURRENT-STATE" or "STATE-CHANGE")
        {
            if (!root.TryGetProperty("product-state", out var ps)) return result;

            AddBool(result, ps, "fpwr", "power");
            AddInt(result, ps, "fnsp", "fan_speed");
            AddBool(result, ps, "auto", "auto");
            AddBool(result, ps, "nmod", "night_mode");
            AddBool(result, ps, "oson", "oscillation");
            AddBool(result, ps, "rhtm", "continuous_monitoring");
            AddInt(result, ps, "sltm", "sleep_timer");
            AddInt(result, ps, "hflr", "hepa_filter");
            AddInt(result, ps, "cflr", "carbon_filter");

            if (feature == DysonFeature.HotCool)
            {
                AddBool(result, ps, "hmod", "heat");       // "HEAT"/"OFF" handled below
                AddKelvin(result, ps, "hmax", "target_temperature");
            }
            if (feature == DysonFeature.HumidifyCool)
            {
                if (TryReadString(ps, "hume", out var hume)) result.Add(("humidify", hume == "HUMD"));
                AddBool(result, ps, "haut", "auto_humidify");
                AddInt(result, ps, "humt", "target_humidity");
            }
        }
        else if (msg is "ENVIRONMENTAL-CURRENT-SENSOR-DATA")
        {
            if (!root.TryGetProperty("data", out var data)) return result;
            AddInt(result, data, "pm25", "pm25");
            AddInt(result, data, "pm10", "pm10");
            AddInt(result, data, "va10", "voc");
            AddInt(result, data, "noxl", "no2");
            AddInt(result, data, "hact", "humidity");
            AddKelvin(result, data, "tact", "temperature");
        }

        return result;
    }

    private static bool TryReadString(JsonElement parent, string field, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(field, out var el)) return false;
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

    private static void AddBool(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (TryReadString(p, field, out var s) && (s is "ON" or "OFF" or "HEAT" or "HUMD"))
            r.Add((key, s is "ON" or "HEAT" or "HUMD"));
    }

    private static void AddInt(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (TryReadString(p, field, out var s) && int.TryParse(s, out var n))
            r.Add((key, (double)n));
    }

    private static void AddKelvin(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (TryReadString(p, field, out var s) && double.TryParse(s, out var deciK) && deciK > 0)
            r.Add((key, Math.Round(deciK / 10.0 - 273.15, 1, MidpointRounding.AwayFromZero)));
    }
}
