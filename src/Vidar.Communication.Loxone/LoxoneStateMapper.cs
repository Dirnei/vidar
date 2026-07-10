using System.Text.Json;

namespace Vidar.Communication.Loxone;

// Maps a control's flattened state payload (as republished by loxone2mqtt) to capability
// updates. Keyed by control type because a field like "active" means power on a Switch but
// "occupied" on a presence detector. Present-fields only: unknown types / missing fields yield
// no updates. Mirrors DreoStateMapper.
public static class LoxoneStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapState(string controlType, string payload)
    {
        var result = new List<(string, object)>();
        if (string.IsNullOrWhiteSpace(payload)) return result;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { return result; }
        using (doc)
        {
            var p = doc.RootElement;
            if (p.ValueKind != JsonValueKind.Object) return result;

            switch (controlType)
            {
                case "Switch":
                case "Pushbutton":
                    AddBool(result, p, "active", "switch");
                    break;
                case "Dimmer":
                    AddLight(result, p);
                    break;
                case "LightControllerV2":
                    AddNumber(result, p, "activeMood", "mode");
                    // On/off master as the composite `light` value ({on}); no brightness of its own.
                    AddLight(result, p);
                    break;
                case "PresenceDetector":
                    AddBool(result, p, "active", "presence");
                    AddNumber(result, p, "brightness", "brightness");
                    break;
                case "SmokeAlarm":
                    AddBool(result, p, "active", "smoke");
                    AddNumber(result, p, "battery", "battery");
                    AddBool(result, p, "tamper", "tamper");
                    break;
                case "Touch":
                    AddText(result, p, "action", "action");
                    break;
                case "ColorPickerRGBW":
                    AddLight(result, p);
                    AddText(result, p, "color", "light_color");
                    AddNumber(result, p, "white", "light_white");
                    break;
                case "ColorPickerTunableWhite":
                    AddLight(result, p);
                    AddNumber(result, p, "colortemp", "light_color_temp");
                    break;
                case "RoomControllerV2":
                    AddNumber(result, p, "tempActual", "temperature");
                    AddNumber(result, p, "tempTarget", "target_temp");
                    AddNumber(result, p, "mode", "climate_mode");
                    AddNumber(result, p, "valve", "valve");
                    break;
            }
        }
        return result;
    }

    // Composite light card reads {on, brightness}. Dimmer "position" is 0..100.
    private static void AddLight(List<(string, object)> r, JsonElement p)
    {
        if (!p.TryGetProperty("active", out var onEl)) return;
        var light = new Dictionary<string, object> { ["on"] = ToBool(onEl) };
        if (p.TryGetProperty("position", out var posEl) && posEl.ValueKind == JsonValueKind.Number
            && posEl.TryGetDouble(out var pos))
            light["brightness"] = pos;
        r.Add(("light", light));
    }

    private static void AddBool(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var el)) r.Add((key, ToBool(el)));
    }

    private static void AddNumber(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.Number
            && el.TryGetDouble(out var d))
            r.Add((key, d));
    }

    private static void AddText(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var el) && el.ValueKind == JsonValueKind.String)
            r.Add((key, el.GetString() ?? ""));
    }

    private static bool ToBool(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetDouble(out var d) && d != 0,
        _ => false,
    };
}
