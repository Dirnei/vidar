using System.Text.Json;
using Vidar.Communication.Shelly;

namespace Vidar.Communication.Shelly.Tests;

public class ShellyStateMapperTests
{
    [Fact]
    public void MapGen2Status_Switch_ExtractsState()
    {
        var json = """{"id":0,"source":"init","output":true,"apower":23.5,"voltage":230.1,"aenergy":{"total":12.34}}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapSwitchStatus(doc.RootElement);
        Assert.Contains(updates, u => u.CapabilityKey == "switch" && (bool)u.Value == true);
        Assert.Contains(updates, u => u.CapabilityKey == "power" && (double)u.Value == 23.5);
        Assert.Contains(updates, u => u.CapabilityKey == "energy" && (double)u.Value == 12.34 / 1000.0);
    }

    [Fact]
    public void MapGen2Status_Cover_ExtractsPosition()
    {
        var json = """{"id":0,"source":"init","state":"open","current_pos":65}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapCoverStatus(doc.RootElement);
        Assert.Contains(updates, u => u.CapabilityKey == "cover" && (int)u.Value == 65);
    }

    [Fact]
    public void MapGen2Status_Cover_ExtractsPowerAndEnergy()
    {
        var json = """{"id":1,"state":"open","apower":12.5,"aenergy":{"total":7885.0},"current_pos":100}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapCoverStatus(doc.RootElement);
        Assert.Contains(updates, u => u.CapabilityKey == "cover" && (int)u.Value == 100);
        Assert.Contains(updates, u => u.CapabilityKey == "power" && (double)u.Value == 12.5);
        Assert.Contains(updates, u => u.CapabilityKey == "energy" && (double)u.Value == 7885.0 / 1000.0);
    }

    [Fact]
    public void MapGen2Status_Cover_WithoutPosition_StillExtractsPower()
    {
        // An uncalibrated channel (pos_control:false) reports no current_pos.
        var json = """{"id":0,"state":"stopped","apower":0.0,"aenergy":{"total":0.0}}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapCoverStatus(doc.RootElement);
        Assert.DoesNotContain(updates, u => u.CapabilityKey == "cover");
        Assert.Contains(updates, u => u.CapabilityKey == "power" && (double)u.Value == 0.0);
    }

    [Fact]
    public void MapGen2Status_Temperature_ExtractsValue()
    {
        var json = """{"id":0,"tC":22.4,"tF":72.3}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapTemperatureStatus(doc.RootElement);
        Assert.Contains(updates, u => u.CapabilityKey == "temperature" && (double)u.Value == 22.4);
    }

    [Fact]
    public void MapGen2Status_Light_ExtractsCompositeOnAndBrightness()
    {
        var json = """{"id":0,"source":"WS_in","output":true,"brightness":75.0,"temperature":{"tC":45.2}}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapLightStatus(doc.RootElement);

        var light = Assert.Single(updates, u => u.CapabilityKey == "light");
        var state = Assert.IsType<Dictionary<string, object>>(light.Value);
        Assert.Equal(true, state["on"]);
        Assert.Equal(75.0, state["brightness"]);
    }

    [Fact]
    public void MapGen2Status_Light_WithPowerMeter_ExtractsPowerAndEnergy()
    {
        var json = """{"id":0,"output":false,"brightness":40.0,"apower":12.3,"aenergy":{"total":1500.0}}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapLightStatus(doc.RootElement);

        Assert.Contains(updates, u => u.CapabilityKey == "power" && (double)u.Value == 12.3);
        Assert.Contains(updates, u => u.CapabilityKey == "energy" && (double)u.Value == 1500.0 / 1000.0);
    }

    [Fact]
    public void MapGen1Light_ExtractsCompositeOnAndBrightness()
    {
        var json = """{"ison":true,"source":"http","mode":"white","brightness":60}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapGen1LightStatus(doc.RootElement);

        var light = Assert.Single(updates, u => u.CapabilityKey == "light");
        var state = Assert.IsType<Dictionary<string, object>>(light.Value);
        Assert.Equal(true, state["on"]);
        Assert.Equal(60.0, state["brightness"]);
    }
}
