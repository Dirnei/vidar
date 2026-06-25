using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonStateMapperTests
{
    [Fact]
    public void MapState_CurrentState_MapsControls()
    {
        var payload = """
        {"msg":"CURRENT-STATE","product-state":{
          "fpwr":"ON","fnsp":"0005","auto":"OFF","nmod":"OFF","oson":"ON",
          "hflr":"0080","cflr":"0075","rhtm":"ON"}}
        """;

        var updates = DysonStateMapper.MapState(payload).ToDictionary(u => u.CapabilityKey, u => u.Value);

        Assert.Equal(true, updates["power"]);
        Assert.Equal(5d, updates["fan_speed"]);
        Assert.Equal(false, updates["auto"]);
        Assert.Equal(true, updates["oscillation"]);
        Assert.Equal(80d, updates["hepa_filter"]);
        Assert.Equal(75d, updates["carbon_filter"]);
    }

    [Fact]
    public void MapState_Environmental_MapsSensorsWithKelvinConversion()
    {
        var payload = """
        {"msg":"ENVIRONMENTAL-CURRENT-SENSOR-DATA","data":{
          "pm25":"0012","pm10":"0008","va10":"0030","noxl":"0005",
          "tact":"2980","hact":"0045"}}
        """;

        var updates = DysonStateMapper.MapState(payload).ToDictionary(u => u.CapabilityKey, u => u.Value);

        Assert.Equal(12d, updates["pm25"]);
        Assert.Equal(8d, updates["pm10"]);
        Assert.Equal(24.9d, (double)updates["temperature"], 1); // 2980/10 K = 298.0K = 24.85C -> round half away from zero
        Assert.Equal(45d, updates["humidity"]);
    }

    [Theory]
    [InlineData("2980", 24.9)] // 24.85 -> half away from zero
    [InlineData("3000", 26.9)] // 26.85 -> half away from zero
    [InlineData("2965", 23.4)] // 23.35 -> half away from zero
    public void MapState_Environmental_TemperatureRoundsHalfAwayFromZero(string tact, double expected)
    {
        var payload = $"{{\"msg\":\"ENVIRONMENTAL-CURRENT-SENSOR-DATA\",\"data\":{{\"tact\":\"{tact}\"}}}}";

        var updates = DysonStateMapper.MapState(payload).ToDictionary(u => u.CapabilityKey, u => u.Value);

        Assert.Equal(expected, (double)updates["temperature"], 1);
    }

    [Fact]
    public void MapState_StateChange_UsesNewValueOfPair()
    {
        // STATE-CHANGE encodes values as ["old","new"] arrays
        var payload = """
        {"msg":"STATE-CHANGE","product-state":{"fnsp":["0003","0007"],"fpwr":["OFF","ON"]}}
        """;

        var updates = DysonStateMapper.MapState(payload).ToDictionary(u => u.CapabilityKey, u => u.Value);

        Assert.Equal(7d, updates["fan_speed"]);
        Assert.Equal(true, updates["power"]);
    }
}
