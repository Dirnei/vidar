using System.Globalization;
using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Bambu;

public static class BambuStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> Map(string reportJson)
    {
        var result = new List<(string, object)>();
        using var doc = JsonDocument.Parse(reportJson);
        if (!doc.RootElement.TryGetProperty("print", out var p) || p.ValueKind != JsonValueKind.Object)
            return result;

        AddString(result, p, "gcode_state", "state");
        AddNumber(result, p, "mc_percent", "progress");
        AddNumber(result, p, "mc_remaining_time", "time_remaining");
        AddNumber(result, p, "layer_num", "layer_current");
        AddNumber(result, p, "total_layer_num", "layer_total");
        AddNumber(result, p, "nozzle_temper", "nozzle_temp");
        AddNumber(result, p, "nozzle_target_temper", "nozzle_target");
        AddNumber(result, p, "bed_temper", "bed_temp");
        AddNumber(result, p, "bed_target_temper", "bed_target");
        AddNumber(result, p, "chamber_temper", "chamber_temp");
        AddFanPercent(result, p, "cooling_fan_speed", "fan_cooling");
        AddFanPercent(result, p, "big_fan1_speed", "fan_aux");
        AddFanPercent(result, p, "big_fan2_speed", "fan_chamber");
        AddNumber(result, p, "spd_lvl", "print_speed_profile");
        AddStringField(result, p, "subtask_name", "gcode_file", "job_name");
        AddString(result, p, "wifi_signal", "wifi_signal");
        AddParsedNumberString(result, p, "nozzle_diameter", "nozzle_diameter");
        AddChamberLight(result, p);
        AddHms(result, p);
        AddPrintError(result, p);
        AddAms(result, p);
        return result;
    }

    private static void AddString(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var e) && e.ValueKind == JsonValueKind.String)
            r.Add((key, e.GetString()!));
    }

    private static void AddStringField(List<(string, object)> r, JsonElement p, string field, string fallback, string key)
    {
        if (p.TryGetProperty(field, out var e) && e.ValueKind == JsonValueKind.String && e.GetString()!.Length > 0)
            r.Add((key, e.GetString()!));
        else if (p.TryGetProperty(fallback, out var f) && f.ValueKind == JsonValueKind.String && f.GetString()!.Length > 0)
            r.Add((key, f.GetString()!));
    }

    private static void AddNumber(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var e) && e.ValueKind == JsonValueKind.Number)
            r.Add((key, e.GetDouble()));
    }

    private static void AddParsedNumberString(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var e) && e.ValueKind == JsonValueKind.String
            && double.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            r.Add((key, v));
    }

    private static void AddFanPercent(List<(string, object)> r, JsonElement p, string field, string key)
    {
        if (p.TryGetProperty(field, out var e) && e.ValueKind == JsonValueKind.String
            && int.TryParse(e.GetString(), out var gear))
            r.Add((key, (double)Math.Round(gear / 15.0 * 100.0)));
    }

    private static void AddChamberLight(List<(string, object)> r, JsonElement p)
    {
        if (!p.TryGetProperty("lights_report", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var l in arr.EnumerateArray())
            if (l.TryGetProperty("node", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() == "chamber_light"
                && l.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
                r.Add(("light_chamber", m.GetString() == "on"));
    }

    private static void AddHms(List<(string, object)> r, JsonElement p)
    {
        if (!p.TryGetProperty("hms", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        var entries = arr.EnumerateArray().ToList();
        r.Add(("has_error", entries.Count > 0));
        if (entries.Count == 0) return;
        var first = entries[0];
        if (first.TryGetProperty("attr", out var a) && a.ValueKind == JsonValueKind.Number
            && first.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
            r.Add(("hms_error", BambuHmsCatalog.Describe(a.GetInt64(), c.GetInt64())));
    }

    private static void AddPrintError(List<(string, object)> r, JsonElement p)
    {
        if (p.TryGetProperty("print_error", out var e) && e.ValueKind == JsonValueKind.Number && e.GetInt64() != 0)
            r.Add(("print_error", e.GetInt64().ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddAms(List<(string, object)> r, JsonElement p)
    {
        if (!p.TryGetProperty("ams", out var ams) || ams.ValueKind != JsonValueKind.Object) return;
        if (ams.TryGetProperty("tray_now", out var tn))
        {
            if (tn.ValueKind == JsonValueKind.String && int.TryParse(tn.GetString(), out var active))
                r.Add(("ams_active_tray", (double)active));
            else if (tn.ValueKind == JsonValueKind.Number)
                r.Add(("ams_active_tray", (double)tn.GetInt32()));
        }
        if (!ams.TryGetProperty("ams", out var units) || units.ValueKind != JsonValueKind.Array) return;

        var trayIndex = 0;
        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("tray", out var trays) || trays.ValueKind != JsonValueKind.Array) continue;
            foreach (var t in trays.EnumerateArray())
            {
                if (t.TryGetProperty("tray_type", out var ty) && ty.ValueKind == JsonValueKind.String && ty.GetString()!.Length > 0)
                    r.Add(($"ams_tray_{trayIndex}_type", BambuHmsCatalog.FilamentName(ty.GetString()!)));
                if (t.TryGetProperty("tray_color", out var col) && col.ValueKind == JsonValueKind.String)
                    r.Add(($"ams_tray_{trayIndex}_color", col.GetString()!));
                if (t.TryGetProperty("remain", out var rem) && rem.ValueKind == JsonValueKind.Number)
                    r.Add(($"ams_tray_{trayIndex}_remain", rem.GetDouble()));
                trayIndex++;
            }
        }
    }

    private static readonly Dictionary<string, (string Label, UnitType Unit)> SensorMeta = new()
    {
        ["state"] = ("State", UnitType.Text),
        ["progress"] = ("Progress", UnitType.Percent),
        ["time_remaining"] = ("Time Remaining", UnitType.Minutes),
        ["layer_current"] = ("Layer", UnitType.Number),
        ["layer_total"] = ("Total Layers", UnitType.Number),
        ["nozzle_temp"] = ("Nozzle Temp", UnitType.Celsius),
        ["bed_temp"] = ("Bed Temp", UnitType.Celsius),
        ["chamber_temp"] = ("Chamber Temp", UnitType.Celsius),
        ["fan_cooling"] = ("Part Cooling Fan", UnitType.Percent),
        ["fan_aux"] = ("Aux Fan", UnitType.Percent),
        ["fan_chamber"] = ("Chamber Fan", UnitType.Percent),
        ["job_name"] = ("Job", UnitType.Text),
        ["wifi_signal"] = ("Wi-Fi Signal", UnitType.Text),
        ["nozzle_diameter"] = ("Nozzle Diameter", UnitType.Number),
        ["hms_error"] = ("HMS Error", UnitType.Text),
        ["has_error"] = ("Has Error", UnitType.YesNo),
        ["print_error"] = ("Print Error", UnitType.Text),
        ["ams_active_tray"] = ("Active AMS Tray", UnitType.Number),
    };

    // Commandable capabilities are fixed (always offered), independent of the payload.
    private static readonly List<CapabilityDescriptor> CommandCaps = new()
    {
        Cmd("print_pause", "Pause", UnitType.Action),
        Cmd("print_resume", "Resume", UnitType.Action),
        Cmd("print_stop", "Stop", UnitType.Action),
        Cmd("light_chamber", "Chamber Light", UnitType.OnOff),
        new() { Key = "print_speed_profile", Label = "Speed", Unit = UnitType.Number, Commandable = true, Min = 1, Max = 4 },
        new() { Key = "nozzle_target", Label = "Nozzle Target", Unit = UnitType.Celsius, Commandable = true, Min = 0, Max = 300 },
        new() { Key = "bed_target", Label = "Bed Target", Unit = UnitType.Celsius, Commandable = true, Min = 0, Max = 120 },
        new() { Key = "fan_cooling", Label = "Part Cooling Fan", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 },
        Cmd("home", "Home Axes", UnitType.Action),
        Cmd("ams_load", "Load Filament", UnitType.Action),
        Cmd("ams_unload", "Unload Filament", UnitType.Action),
        Cmd("camera_snapshot", "Take Snapshot", UnitType.Action),
    };

    private static CapabilityDescriptor Cmd(string key, string label, UnitType unit) =>
        new() { Key = key, Label = label, Unit = unit, Commandable = true };

    public static List<CapabilityDescriptor> BuildCapabilities(string reportJson)
    {
        var caps = new List<CapabilityDescriptor>();
        var present = Map(reportJson).Select(x => x.CapabilityKey).ToHashSet();
        var commandKeys = CommandCaps.Select(c => c.Key).ToHashSet();

        // AMS tray keys are dynamic — emit a descriptor for each observed tray key.
        foreach (var key in present)
        {
            if (commandKeys.Contains(key)) continue;             // covered by CommandCaps
            if (SensorMeta.TryGetValue(key, out var meta))
                caps.Add(new CapabilityDescriptor { Key = key, Label = meta.Label, Unit = meta.Unit });
            else if (key.StartsWith("ams_tray_"))
                caps.Add(new CapabilityDescriptor { Key = key, Label = AmsTrayLabel(key), Unit = AmsTrayUnit(key) });
        }
        caps.AddRange(CommandCaps);
        return caps;
    }

    private static UnitType AmsTrayUnit(string key) => key.EndsWith("_remain") ? UnitType.Percent : UnitType.Text;
    private static string AmsTrayLabel(string key)
    {
        var parts = key.Split('_'); // ams_tray_{n}_{field}
        return $"AMS Tray {parts[2]} {char.ToUpper(parts[3][0])}{parts[3][1..]}";
    }
}
