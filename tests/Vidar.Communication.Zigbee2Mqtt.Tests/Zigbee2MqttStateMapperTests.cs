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

    [Fact]
    public void MapState_UpdateAvailable_ExtractsState()
    {
        var json = """{"update":{"state":"available","installed_version":16779265,"latest_version":16779521}}""";
        var known = new List<CapabilityType> { CapabilityType.Update };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        var updateEntry = Assert.Single(updates, u => u.Capability == CapabilityType.Update);
        var dict = Assert.IsType<Dictionary<string, object>>(updateEntry.Value);
        Assert.Equal("available", dict["state"]);
        Assert.Equal(16779265L, dict["installed_version"]);
        Assert.Equal(16779521L, dict["latest_version"]);
    }

    [Fact]
    public void MapState_UpdateIdle_ExtractsState()
    {
        var json = """{"update":{"state":"idle","installed_version":16779265}}""";
        var known = new List<CapabilityType> { CapabilityType.Update };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        var updateEntry = Assert.Single(updates, u => u.Capability == CapabilityType.Update);
        var dict = Assert.IsType<Dictionary<string, object>>(updateEntry.Value);
        Assert.Equal("idle", dict["state"]);
    }

    [Fact]
    public void MapState_UpdateParsed_EvenWhenNotInCapabilities()
    {
        var json = """{"update":{"state":"available"},"temperature":22.0}""";
        var known = new List<CapabilityType> { CapabilityType.Temperature };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.Contains(updates, u => u.Capability == CapabilityType.Update);
    }

    [Fact]
    public void MapState_UpdateNotDuplicatedInExtras()
    {
        var json = """{"update":{"state":"available"},"linkquality":50}""";
        var known = new List<CapabilityType> { CapabilityType.Update };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        var extras = updates.FirstOrDefault(u => u.Capability == CapabilityType.Extras);
        if (extras != null)
        {
            var dict = (Dictionary<string, object>)extras.Value;
            Assert.False(dict.ContainsKey("update"));
        }
    }
}
