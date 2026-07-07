using System.IO;
using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

// Validates DreoStateMapper against a REAL flattened device/state capture from a live
// DR-HCF001S (data.mixed unwrapped by the sidecar). Meta fields (muteon, wifi_rssi) must be
// ignored by present-fields-only mapping.
public class DreoStateMapperFixtureTests
{
    [Fact]
    public void MapState_CeilingFanFixture_MapsFanAndLight()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cloud-ceilingfan-state.json");
        var json = File.ReadAllText(path);

        var u = DreoStateMapper.MapState(json).ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.Equal(true, u["fan"]);              // fanon
        Assert.Equal(3d, u["fan_speed"]);          // windlevel
        Assert.Equal(2d, u["mode"]);               // mode (int)
        var light = Assert.IsType<Dictionary<string, object>>(u["light"]);  // composite
        Assert.Equal(false, light["on"]);          // lighton
        Assert.Equal(1d, light["brightness"]);     // brightness
        Assert.Equal(16d, u["light_color_temp"]);  // colortemp
        Assert.False(u.ContainsKey("power"));      // mcuon read-only, not mapped
        Assert.False(u.ContainsKey("muteon"));     // meta field ignored
    }
}
