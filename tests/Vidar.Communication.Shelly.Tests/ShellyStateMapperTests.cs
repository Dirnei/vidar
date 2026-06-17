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
    public void MapGen2Status_Temperature_ExtractsValue()
    {
        var json = """{"id":0,"tC":22.4,"tF":72.3}""";
        var doc = JsonDocument.Parse(json);
        var updates = ShellyStateMapper.MapTemperatureStatus(doc.RootElement);
        Assert.Contains(updates, u => u.CapabilityKey == "temperature" && (double)u.Value == 22.4);
    }
}
