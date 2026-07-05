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
            "power" => new Dictionary<string, object> { ["poweron"] = ToBool(value) },
            "fan" => new Dictionary<string, object> { ["fanon"] = ToBool(value) },
            "fan_speed" => new Dictionary<string, object> { ["windlevel"] = ToInt(value) },
            "mode" => new Dictionary<string, object> { ["windtype"] = ToStr(value) },
            "light" => new Dictionary<string, object> { ["lighton"] = ToBool(value) },
            "light_brightness" => new Dictionary<string, object> { ["brightness"] = ToInt(value) },
            "light_color_temp" => new Dictionary<string, object> { ["colortemp"] = ToInt(value) },
            // direction: add here once the real key is known (E2E task).
            _ => null,
        };
        return param is null ? null : JsonSerializer.Serialize(param);
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

    private static string ToStr(object v) => v?.ToString() ?? "";
}
