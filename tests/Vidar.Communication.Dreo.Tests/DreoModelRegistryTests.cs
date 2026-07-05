using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

public class DreoModelRegistryTests
{
    [Fact]
    public void Resolve_AnyModel_IncludesFanAndLightCapabilities()
    {
        var profile = DreoModelRegistry.Resolve("DR-HCF001S");
        var keys = profile.Capabilities.Select(c => c.Key).ToHashSet();

        Assert.Contains("power", keys);
        Assert.Contains("fan", keys);
        Assert.Contains("fan_speed", keys);
        Assert.Contains("mode", keys);
        Assert.Contains("direction", keys);
        Assert.Contains("light", keys);
        Assert.Contains("light_brightness", keys);
        Assert.Contains("light_color_temp", keys);
    }

    [Fact]
    public void Resolve_FanSpeed_IsCommandableNumberWithRange()
    {
        var speed = DreoModelRegistry.Resolve("DR-HCF001S")
            .Capabilities.Single(c => c.Key == "fan_speed");

        Assert.Equal("Number", speed.Unit);
        Assert.True(speed.Commandable);
        Assert.Equal(1d, speed.Min);
        Assert.Equal(6d, speed.Max);
    }

    [Fact]
    public void Resolve_Direction_IsCommandableText()
    {
        var dir = DreoModelRegistry.Resolve("DR-HCF001S")
            .Capabilities.Single(c => c.Key == "direction");

        Assert.Equal("Text", dir.Unit);
        Assert.True(dir.Commandable);
    }
}
