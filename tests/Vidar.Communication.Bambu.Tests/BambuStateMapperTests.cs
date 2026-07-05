using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuStateMapperTests
{
    private const string FullReport = """
    { "print": {
        "gcode_state": "RUNNING",
        "mc_percent": 42,
        "mc_remaining_time": 88,
        "layer_num": 120,
        "total_layer_num": 300,
        "nozzle_temper": 220.0,
        "nozzle_target_temper": 220.0,
        "bed_temper": 60.0,
        "bed_target_temper": 60.0,
        "chamber_temper": 32.0,
        "cooling_fan_speed": "15",
        "spd_lvl": 2,
        "subtask_name": "benchy.3mf",
        "nozzle_diameter": "0.4",
        "lights_report": [ { "node": "chamber_light", "mode": "on" } ],
        "hms": [ { "attr": 50331904, "code": 65540 } ],
        "ams": { "tray_now": "0", "ams": [ { "tray": [ { "tray_type": "PLA", "tray_color": "FF0000FF", "remain": 75 } ] } ] }
    } }
    """;

    private static Dictionary<string, object> Map(string json) =>
        BambuStateMapper.Map(json).ToDictionary(x => x.CapabilityKey, x => x.Value);

    [Fact]
    public void Map_FullReport_MapsCoreFields()
    {
        var u = Map(FullReport);
        Assert.Equal("RUNNING", u["state"]);
        Assert.Equal(42d, u["progress"]);
        Assert.Equal(88d, u["time_remaining"]);
        Assert.Equal(120d, u["layer_current"]);
        Assert.Equal(220d, u["nozzle_temp"]);
        Assert.Equal(32d, u["chamber_temp"]);
        Assert.Equal(100d, u["fan_cooling"]);        // 15/15*100
        Assert.Equal(2d, u["print_speed_profile"]);
        Assert.Equal("benchy.3mf", u["job_name"]);
        Assert.Equal(0.4d, u["nozzle_diameter"]);
        Assert.Equal(true, u["light_chamber"]);
        Assert.Equal(true, u["has_error"]);
        Assert.Equal("PLA", u["ams_tray_0_type"]);
        Assert.Equal(0d, u["ams_active_tray"]);
    }

    [Fact]
    public void Map_PartialDelta_OnlyEmitsPresentFields()
    {
        var u = Map("""{ "print": { "mc_percent": 55 } }""");
        Assert.Equal(55d, u["progress"]);
        Assert.DoesNotContain("nozzle_temp", u.Keys);
        Assert.DoesNotContain("chamber_temp", u.Keys);   // absent on P1/A1 too
        Assert.Single(u);
    }

    [Fact]
    public void Map_AmsTrayNowAsNumber_DoesNotThrow()
    {
        const string json = """
        { "print": {
            "mc_percent": 10,
            "ams": { "tray_now": 0, "ams": [ { "tray": [ { "tray_type": "PLA", "tray_color": "FF0000FF", "remain": 75 } ] } ] }
        } }
        """;

        var u = Map(json);
        Assert.Equal(0d, u["ams_active_tray"]);

        var caps = BambuStateMapper.BuildCapabilities(json);
        Assert.Contains(caps, c => c.Key == "ams_active_tray");
    }

    [Fact]
    public void Map_NonPrintPayload_ReturnsEmpty()
    {
        Assert.Empty(BambuStateMapper.Map("""{ "system": { "command": "get_version" } }"""));
    }

    [Fact]
    public void BuildCapabilities_EmitsDescriptorsForPresentFieldsPlusCommands()
    {
        var caps = BambuStateMapper.BuildCapabilities(FullReport);
        var keys = caps.Select(c => c.Key).ToHashSet();
        Assert.Contains("progress", keys);
        Assert.Contains("chamber_temp", keys);          // present in FullReport
        Assert.Contains("print_pause", keys);           // fixed command capability
        Assert.Contains("camera_snapshot", keys);
        Assert.True(caps.Single(c => c.Key == "progress").Unit == Vidar.Core.Capabilities.UnitType.Percent);
        Assert.True(caps.Single(c => c.Key == "time_remaining").Unit == Vidar.Core.Capabilities.UnitType.Minutes);
        Assert.True(caps.Single(c => c.Key == "nozzle_target").Commandable);
    }
}
