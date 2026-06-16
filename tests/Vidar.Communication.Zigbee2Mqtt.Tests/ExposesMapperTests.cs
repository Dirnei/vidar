using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt.Tests;

public class ExposesMapperTests
{
    [Fact]
    public void MapExposes_Light_ReturnsCorrectCapabilities()
    {
        var json = """[{"type":"light","features":[{"type":"binary","name":"state","access":7},{"type":"numeric","name":"brightness","access":7}]}]""";
        var caps = ExposesMapper.MapCapabilities(JsonDocument.Parse(json).RootElement);
        Assert.Contains(CapabilityType.Switch, caps);
        Assert.Contains(CapabilityType.Dimmer, caps);
    }

    [Fact]
    public void MapExposes_MotionSensor_ReturnsCorrectCapabilities()
    {
        var json = """[{"type":"binary","name":"occupancy","access":1},{"type":"numeric","name":"temperature","access":1}]""";
        var caps = ExposesMapper.MapCapabilities(JsonDocument.Parse(json).RootElement);
        Assert.Contains(CapabilityType.Motion, caps);
        Assert.Contains(CapabilityType.Temperature, caps);
    }

    [Fact]
    public void MapExposes_Cover_ReturnsCorrectCapabilities()
    {
        var json = """[{"type":"cover","features":[{"type":"binary","name":"state","access":7},{"type":"numeric","name":"position","access":7}]}]""";
        var caps = ExposesMapper.MapCapabilities(JsonDocument.Parse(json).RootElement);
        Assert.Contains(CapabilityType.Cover, caps);
    }

    [Fact]
    public void MapExposes_PowerSensor_ReturnsCorrectCapabilities()
    {
        var json = """[{"type":"numeric","name":"power","access":1,"unit":"W"},{"type":"numeric","name":"energy","access":1,"unit":"kWh"}]""";
        var caps = ExposesMapper.MapCapabilities(JsonDocument.Parse(json).RootElement);
        Assert.Contains(CapabilityType.Power, caps);
        Assert.Contains(CapabilityType.Energy, caps);
    }

    [Fact]
    public void MapExposes_Update_ReturnsUpdateCapability()
    {
        var json = """[{"type":"update","features":[{"type":"binary","name":"update_available","access":1},{"type":"numeric","name":"update_progress","access":1}]}]""";
        var caps = ExposesMapper.MapCapabilities(JsonDocument.Parse(json).RootElement);
        Assert.Contains(CapabilityType.Update, caps);
    }
}
