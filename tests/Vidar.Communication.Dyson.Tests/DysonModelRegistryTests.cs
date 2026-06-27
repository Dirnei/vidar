using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonModelRegistryTests
{
    [Theory]
    [InlineData("438", DysonFeature.Base)]
    [InlineData("438K", DysonFeature.Base)]
    [InlineData("527", DysonFeature.HotCool)]
    [InlineData("527K", DysonFeature.HotCool)]
    [InlineData("455", DysonFeature.HotCool)]
    [InlineData("358", DysonFeature.HumidifyCool)]
    [InlineData("358K", DysonFeature.HumidifyCool)]
    [InlineData("999-UNKNOWN", DysonFeature.Base)] // fallback
    public void Resolve_MapsProductTypeToFeature(string productType, DysonFeature expected)
    {
        Assert.Equal(expected, DysonModelRegistry.Resolve(productType).Feature);
    }

    [Fact]
    public void Resolve_HumidifyCool_IncludesHumidifierCapabilities()
    {
        var keys = DysonModelRegistry.Resolve("358K").Capabilities.Select(c => c.Key).ToHashSet();
        Assert.Contains("power", keys);
        Assert.Contains("fan_speed", keys);
        Assert.Contains("humidify", keys);
        Assert.Contains("target_humidity", keys);
        Assert.Contains("pm25", keys);
    }

    [Fact]
    public void Resolve_BaseModel_HasNoHeatOrHumidify()
    {
        var keys = DysonModelRegistry.Resolve("438").Capabilities.Select(c => c.Key).ToHashSet();
        Assert.DoesNotContain("humidify", keys);
        Assert.DoesNotContain("heat", keys);
    }

    [Theory]
    [InlineData("N223", false)]
    [InlineData("276", false)]
    [InlineData("277", false)]
    [InlineData("358K", true)]
    public void IsSupported_ExcludesRobots(string productType, bool expected)
    {
        Assert.Equal(expected, DysonModelRegistry.IsSupported(productType));
    }
}
