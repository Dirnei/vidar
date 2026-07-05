using System.Text.Json;

namespace Vidar.Communication.Dreo;

// Maps a flattened Dreo reported-state payload (field keys at the top level, as the
// dreo2mqtt sidecar republishes it) to standardized capability updates. Present-fields
// only: unknown/missing keys are silently skipped so one mapper serves the whole
// ceiling-fan family and tolerates the yet-unresolved `direction` key once added.
public static class DreoStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapState(string payload)
    {
        var result = new List<(string, object)>();
        if (string.IsNullOrWhiteSpace(payload)) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { return result; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;

            AddBool(result, root, "poweron", "power");
            AddBool(result, root, "fanon", "fan");
            AddNumber(result, root, "windlevel", "fan_speed");
            AddText(result, root, "windtype", "mode");
            AddBool(result, root, "lighton", "light");
            AddNumber(result, root, "brightness", "light_brightness");
            AddNumber(result, root, "colortemp", "light_color_temp");
            // direction: field key unknown until live capture — add an AddText/AddBool
            // line here for it in the E2E task once the real payload is observed.
        }
        return result;
    }

    private static void AddBool(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (!p.TryGetProperty(field, out var el)) return;
        if (el.ValueKind == JsonValueKind.True) r.Add((key, true));
        else if (el.ValueKind == JsonValueKind.False) r.Add((key, false));
        else if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) r.Add((key, n != 0));
    }

    private static void AddNumber(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.Number
            && el.TryGetDouble(out var d))
            r.Add((key, d));
    }

    private static void AddText(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (!p.TryGetProperty(field, out var el)) return;
        if (el.ValueKind == JsonValueKind.String) r.Add((key, el.GetString() ?? ""));
        else if (el.ValueKind == JsonValueKind.Number) r.Add((key, el.ToString()));
    }
}
