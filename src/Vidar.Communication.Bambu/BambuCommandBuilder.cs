using System.Globalization;

namespace Vidar.Communication.Bambu;

/// <summary>
/// Translates a fixed, named capability into the printer's request JSON. Gcode/M-codes are an
/// implementation detail HERE ONLY — never a capability, never surfaced to the UI.
/// </summary>
public static class BambuCommandBuilder
{
    public static string? Build(string capabilityKey, object value)
    {
#pragma warning disable CS8321
        static string I(object v) => Convert.ToInt64(Convert.ToDouble(v, CultureInfo.InvariantCulture))
            .ToString(CultureInfo.InvariantCulture);
        static bool B(object v) => v is bool b ? b : Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0;
#pragma warning restore CS8321

        return capabilityKey switch
        {
            "print_pause" => PrintCmd("pause"),
            "print_resume" => PrintCmd("resume"),
            "print_stop" => PrintCmd("stop"),
            "home" => Gcode("G28\n"),
            "nozzle_target" => Gcode($"M104 S{I(value)}\n"),
            "bed_target" => Gcode($"M140 S{I(value)}\n"),
            "fan_cooling" => Gcode($"M106 P1 S{(int)Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture) / 100.0 * 255.0)}\n"),
            "print_speed_profile" => $"{{\"print\":{{\"command\":\"print_speed\",\"param\":\"{I(value)}\",\"sequence_id\":\"0\"}}}}",
            "light_chamber" => $"{{\"system\":{{\"command\":\"ledctrl\",\"led_node\":\"chamber_light\",\"led_mode\":\"{(B(value) ? "on" : "off")}\",\"led_on_time\":500,\"led_off_time\":500,\"loop_times\":0,\"interval_time\":0,\"sequence_id\":\"0\"}}}}",
            "ams_load" => $"{{\"print\":{{\"command\":\"ams_change_filament\",\"target\":255,\"sequence_id\":\"0\"}}}}",
            "ams_unload" => $"{{\"print\":{{\"command\":\"ams_change_filament\",\"target\":254,\"sequence_id\":\"0\"}}}}",
            _ => null, // read-only sensors and camera_snapshot (handled in-actor)
        };
    }

    private static string PrintCmd(string command) =>
        $"{{\"print\":{{\"command\":\"{command}\",\"param\":\"\",\"sequence_id\":\"0\"}}}}";

    private static string Gcode(string line) =>
        $"{{\"print\":{{\"command\":\"gcode_line\",\"param\":\"{line.Replace("\n", "\\n")}\",\"sequence_id\":\"0\"}}}}";
}
