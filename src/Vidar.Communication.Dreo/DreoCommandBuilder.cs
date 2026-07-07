using System.Text.Json;

namespace Vidar.Communication.Dreo;

// Builds the Dreo control `params` object for a capability command. Emits params only;
// the dreo2mqtt sidecar wraps it in {"devicesn","method":"control","params","timestamp"}.
public static class DreoCommandBuilder
{
    public static string? Build(string capabilityKey, object value)
    {
        object? param = capabilityKey switch
        {
            // Control keys mirror the live device's state fields (see DreoStateMapper).
            "fan" => new Dictionary<string, object> { ["fanon"] = ToBool(value) },
            "fan_speed" => new Dictionary<string, object> { ["windlevel"] = ToInt(value) },
            "mode" => new Dictionary<string, object> { ["mode"] = ToInt(value) },
            // Composite light card sends a bool for on/off and a number for brightness.
            "light" => BuildLight(value),
            "light_color_temp" => new Dictionary<string, object> { ["colortemp"] = ToInt(value) },
            _ => null,
        };
        return param is null ? null : JsonSerializer.Serialize(param);
    }

    private static object? BuildLight(object value)
    {
        if (value is bool b) return new Dictionary<string, object> { ["lighton"] = b };
        if (value is string s)
        {
            if (bool.TryParse(s, out var sb)) return new Dictionary<string, object> { ["lighton"] = sb };
            if (double.TryParse(s, out var sd))
                return new Dictionary<string, object> { ["brightness"] = Math.Clamp((int)Math.Round(sd), 1, 100) };
            return null;
        }
        // numeric -> brightness (Dreo brightness range is 1..100)
        return new Dictionary<string, object> { ["brightness"] = Math.Clamp(ToInt(value), 1, 100) };
    }

    private static bool ToBool(object v) => v switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        double d => d != 0,
        _ => false,
    };

    private static int ToInt(object v) => v switch
    {
        double d => (int)Math.Round(d),
        int i => i,
        string s when int.TryParse(s, out var n) => n,
        _ => 0,
    };
}
