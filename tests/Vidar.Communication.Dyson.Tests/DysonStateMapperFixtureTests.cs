using System.IO;
using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

/// <summary>
/// Fixture-driven tests that validate DysonStateMapper against real captured payloads
/// from a live 358K device. The fixture files are copied to the output directory at
/// build time (Content + CopyToOutputDirectory in the csproj).
/// </summary>
public class DysonStateMapperFixtureTests
{
    private static Dictionary<string, object> LoadAndMap(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var json = File.ReadAllText(path);
        return DysonStateMapper.MapState(json, "358K")
            .ToDictionary(x => x.CapabilityKey, x => x.Value);
    }

    [Fact]
    public void MapState_RealCurrentState_358K_MapsAllExpectedKeys()
    {
        var u = LoadAndMap("cloud-358k-current-state.json");

        // Base controls
        Assert.Equal(true, u["power"]);
        Assert.Equal(3d, u["fan_speed"]);          // fnsp = "0003"
        Assert.Equal(false, u["auto"]);
        Assert.Equal(false, u["night_mode"]);
        Assert.Equal(true, u["oscillation"]);       // oson = "ON"
        Assert.Equal(true, u["continuous_monitoring"]);  // rhtm = "ON"
        Assert.Equal(13d, u["hepa_filter"]);        // hflr = "0013"

        // cflr = "INV" → int.TryParse fails → carbon_filter must NOT be present
        Assert.DoesNotContain("carbon_filter", u.Keys);

        // Humidify-specific (358K resolves to HumidifyCool)
        Assert.Equal(true, u["humidify"]);          // hume = "HUMD"
        Assert.Equal(true, u["auto_humidify"]);     // haut = "ON"
        Assert.Equal(50d, u["target_humidity"]);    // humt = "0050"
    }

    [Fact]
    public void MapState_RealEnvironmental_358K_MapsAllExpectedKeys()
    {
        var u = LoadAndMap("cloud-358k-environmental.json");

        Assert.Equal(4d, u["pm25"]);       // pm25 = "0004"
        Assert.Equal(3d, u["pm10"]);       // pm10 = "0003"
        Assert.Equal(12d, u["voc"]);       // va10 = "0012"
        Assert.Equal(2d, u["no2"]);        // noxl = "0002"
        Assert.Equal(45d, u["humidity"]);  // hact = "0045"

        // tact = "2991" → 2991/10.0 K = 299.1 K − 273.15 = 25.95 → rounds to 26.0
        Assert.Equal(26.0d, (double)u["temperature"], 1);
    }
}
