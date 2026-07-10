using System.Globalization;

namespace Vidar.Communication.Loxone;

// Emits the logical command for a control; the loxone2mqtt sidecar wraps it into the WS command
// URL (jdev/sps/io/<uuid>/<command>). Vendor-envelope-free (mirrors DreoCommandBuilder).
public static class LoxoneCommandBuilder
{
    public static string? Build(string capabilityKey, object value) => capabilityKey switch
    {
        "switch" => ToBool(value) ? "on" : "off",
        "light" => BuildLight(value),
        // Loxone selects a LightControllerV2 mood via changeTo/<id>. Emit the full verb here so the
        // sidecar's command_url passes it straight through (the slash disambiguates it from a bare
        // brightness percent).
        "mode" => $"changeTo/{ToInt(value)}",
        // RGBW/tunable-white channels + climate. The sidecar converts these normalized verbs to
        // the real Loxone commands (hsv(...)/temp(...)/setpoint). target_temp keeps a decimal.
        "light_color" => $"color/{ToHex(value)}",
        "light_white" => $"white/{Math.Clamp(ToInt(value), 0, 100).ToString(CultureInfo.InvariantCulture)}",
        "light_color_temp" => $"temp/{ToInt(value).ToString(CultureInfo.InvariantCulture)}",
        "target_temp" => $"settemp/{ToDouble(value).ToString(CultureInfo.InvariantCulture)}",
        "climate_mode" => $"climatemode/{ToInt(value)}",
        _ => null,
    };

    // Composite light: a bool toggles on/off; a number is a brightness percent (0..100).
    private static string BuildLight(object value)
    {
        if (value is bool b) return b ? "on" : "off";
        if (value is string s && bool.TryParse(s, out var sb)) return sb ? "on" : "off";
        return Math.Clamp(ToInt(value), 0, 100).ToString(CultureInfo.InvariantCulture);
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
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => (int)Math.Round(d),
        _ => 0,
    };

    private static double ToDouble(object v) => v switch
    {
        double d => d,
        int i => i,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
        _ => 0,
    };

    // Color arrives as a hex string ("#RRGGBB"); pass it through normalized (uppercase, leading #).
    private static string ToHex(object v)
    {
        var s = v as string ?? "";
        s = s.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return s.ToUpperInvariant();
    }
}
