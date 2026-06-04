using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt.Tests;

public class Zigbee2MqttStateMapperTests
{
    [Fact]
    public void MapState_MotionSensor_ExtractsValues()
    {
        var json = """{"occupancy":true,"temperature":22.4,"battery":95}""";
        var known = new List<CapabilityType> { CapabilityType.Motion, CapabilityType.Temperature };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.Contains(updates, u => u.Capability == CapabilityType.Motion && (bool)u.Value == true);
        Assert.Contains(updates, u => u.Capability == CapabilityType.Temperature && (double)u.Value == 22.4);
        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public void MapState_Light_ExtractsValues()
    {
        var json = """{"state":"ON","brightness":200}""";
        var known = new List<CapabilityType> { CapabilityType.Switch, CapabilityType.Dimmer };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.Contains(updates, u => u.Capability == CapabilityType.Switch && (bool)u.Value == true);
        Assert.Contains(updates, u => u.Capability == CapabilityType.Dimmer);
    }
}
