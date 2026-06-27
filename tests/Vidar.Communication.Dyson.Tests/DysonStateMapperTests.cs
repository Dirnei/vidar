using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonStateMapperTests
{
    [Fact]
    public void MapState_CurrentState_MapsBaseControls()
    {
        var payload = """
        {"msg":"CURRENT-STATE","product-state":{
          "fpwr":"ON","fnsp":"0005","auto":"OFF","nmod":"OFF","oson":"ON",
          "hflr":"0080","cflr":"0075","rhtm":"ON"}}
        """;

        var u = DysonStateMapper.MapState(payload, "438").ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.Equal(true, u["power"]);
        Assert.Equal(5d, u["fan_speed"]);
        Assert.Equal(false, u["auto"]);
        Assert.Equal(true, u["oscillation"]);
        Assert.Equal(80d, u["hepa_filter"]);
        Assert.Equal(75d, u["carbon_filter"]);
    }

    [Fact]
    public void MapState_Environmental_ConvertsTemperatureKelvin()
    {
        var payload = """
        {"msg":"ENVIRONMENTAL-CURRENT-SENSOR-DATA","data":{
          "pm25":"0012","pm10":"0008","va10":"0030","noxl":"0005",
          "tact":"2980","hact":"0045"}}
        """;

        var u = DysonStateMapper.MapState(payload, "438").ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.Equal(12d, u["pm25"]);
        Assert.Equal(8d, u["pm10"]);
        Assert.Equal(24.9d, (double)u["temperature"], 1); // 2980/10 K = 298.0K = 24.85 -> 24.9
        Assert.Equal(45d, u["humidity"]);
    }

    [Fact]
    public void MapState_HumidifyModel_MapsHumidifyFields()
    {
        var payload = """
        {"msg":"CURRENT-STATE","product-state":{
          "fpwr":"ON","hume":"HUMD","haut":"ON","humt":"0050"}}
        """;

        var u = DysonStateMapper.MapState(payload, "358K").ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.Equal(true, u["humidify"]);        // hume == "HUMD"
        Assert.Equal(true, u["auto_humidify"]);   // haut == "ON"
        Assert.Equal(50d, u["target_humidity"]);
    }

    [Fact]
    public void MapState_BaseModel_IgnoresHumidifyFields()
    {
        var payload = """
        {"msg":"CURRENT-STATE","product-state":{"fpwr":"ON","hume":"HUMD"}}
        """;

        var u = DysonStateMapper.MapState(payload, "438").ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.DoesNotContain("humidify", u.Keys);
    }

    [Fact]
    public void MapState_StateChange_UsesNewValueOfPair()
    {
        var payload = """
        {"msg":"STATE-CHANGE","product-state":{"fnsp":["0003","0007"],"fpwr":["OFF","ON"]}}
        """;

        var u = DysonStateMapper.MapState(payload, "438").ToDictionary(x => x.CapabilityKey, x => x.Value);

        Assert.Equal(7d, u["fan_speed"]);
        Assert.Equal(true, u["power"]);
    }
}
