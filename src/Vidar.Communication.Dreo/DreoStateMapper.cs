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

            // Field keys confirmed against a live DR-HCF001S device/state (data.mixed).
            // No `power`: mcuon is read-only and rejected as a command. mode is the int `mode`
            // (values 1 Straight / 2 Natural / 3 Sleep / 4 Reverse). No direction field.
            AddBool(result, root, "fanon", "fan");
            AddNumber(result, root, "windlevel", "fan_speed");
            AddNumber(result, root, "mode", "mode");
            AddLight(result, root);   // composite {on, brightness} for the unified light card
            AddNumber(result, root, "colortemp", "light_color_temp");
        }
        return result;
    }

    // The frontend `light` card is composite: it reads state.light as {on, brightness}. The
    // sidecar always republishes the FULL state, so lighton and brightness arrive together.
    private static void AddLight(List<(string, object)> r, JsonElement p)
    {
        if (!p.TryGetProperty("lighton", out var onEl)) return;
        if (onEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;

        var light = new Dictionary<string, object> { ["on"] = onEl.ValueKind == JsonValueKind.True };
        if (p.TryGetProperty("brightness", out var brEl) && brEl.ValueKind == JsonValueKind.Number
            && brEl.TryGetDouble(out var brightness))
            light["brightness"] = brightness;
        r.Add(("light", light));
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
