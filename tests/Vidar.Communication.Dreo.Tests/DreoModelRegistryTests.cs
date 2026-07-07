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

        Assert.Contains("fan", keys);
        Assert.Contains("fan_speed", keys);
        Assert.Contains("mode", keys);
        Assert.Contains("light", keys);            // composite on/off + brightness
        Assert.Contains("light_color_temp", keys);
        // brightness is folded into the composite `light` capability, not a separate one.
        Assert.DoesNotContain("light_brightness", keys);
        // DR-HCF001S exposes no direction/reverse field, so the profile must not advertise one.
        Assert.DoesNotContain("direction", keys);
    }

    [Fact]
    public void Resolve_FanSpeed_IsCommandableNumberWithRange()
    {
        var speed = DreoModelRegistry.Resolve("DR-HCF001S")
            .Capabilities.Single(c => c.Key == "fan_speed");

        Assert.Equal("Number", speed.Unit);
        Assert.True(speed.Commandable);
        Assert.Equal(1d, speed.Min);
        Assert.Equal(12d, speed.Max);
    }

    [Fact]
    public void Resolve_Mode_IsCommandableEnumWithLabeledOptions()
    {
        var mode = DreoModelRegistry.Resolve("DR-HCF001S")
            .Capabilities.Single(c => c.Key == "mode");

        Assert.Equal("Number", mode.Unit);
        Assert.True(mode.Commandable);
        Assert.NotNull(mode.Options);
        Assert.Equal(4, mode.Options!.Count);
        Assert.Equal("Natural", mode.Options.Single(o => o.Value == 2).Label);
        Assert.Equal("Reverse", mode.Options.Single(o => o.Value == 4).Label);
    }
}
