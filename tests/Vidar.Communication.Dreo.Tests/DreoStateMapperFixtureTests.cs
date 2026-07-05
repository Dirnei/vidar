using System.IO;
using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

// Validates DreoStateMapper against a captured payload. The fixture is currently
// SYNTHETIC (based on PyDreo field keys) and is replaced with a real device capture
// during live E2E — at which point `direction` (and the true `mode` key) are added.
public class DreoStateMapperFixtureTests
{
    [Fact]
    public void MapState_CeilingFanFixture_MapsFanAndLight()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cloud-ceilingfan-state.json");
        var json = File.ReadAllText(path);

        var u = DreoStateMapper.MapState(json).ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.Equal(true, u["power"]);
        Assert.Equal(true, u["fan"]);
        Assert.Equal(4d, u["fan_speed"]);
        Assert.Equal("natural", u["mode"]);
        Assert.Equal(true, u["light"]);
        Assert.Equal(60d, u["light_brightness"]);
        Assert.Equal(35d, u["light_color_temp"]);
    }
}
