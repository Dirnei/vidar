using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt.Tests;

public class Zigbee2MqttStateMapperTests
{
    [Fact]
    public void MapState_MotionSensor_ExtractsValues()
    {
        var json = """{"occupancy":true,"temperature":22.4,"battery":95}""";
        var known = new List<CapabilityDescriptor>
        {
            new() { Key = "motion", Label = "Motion", Unit = UnitType.Detected },
            new() { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius },
        };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.Contains(updates, u => u.CapabilityKey == "motion" && (bool)u.Value == true);
        Assert.Contains(updates, u => u.CapabilityKey == "temperature" && (double)u.Value == 22.4);
        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public void MapState_Light_ExtractsValues()
    {
        var json = """{"state":"ON","brightness":200}""";
        var known = new List<CapabilityDescriptor>
        {
            new() { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true },
            new() { Key = "dimmer", Label = "Dimmer", Unit = UnitType.Percent, Commandable = true },
        };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.Contains(updates, u => u.CapabilityKey == "switch" && (bool)u.Value == true);
        Assert.Contains(updates, u => u.CapabilityKey == "dimmer");
    }

    [Fact]
    public void MapState_UpdateAvailable_ExtractsState()
    {
        var json = """{"update":{"state":"available","installed_version":16779265,"latest_version":16779521}}""";
        var known = new List<CapabilityDescriptor>
        {
            new() { Key = "update", Label = "Update", Unit = UnitType.Text },
        };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        var updateEntry = Assert.Single(updates, u => u.CapabilityKey == "update");
        var dict = Assert.IsType<Dictionary<string, object>>(updateEntry.Value);
        Assert.Equal("available", dict["state"]);
        Assert.Equal(16779265L, dict["installed_version"]);
        Assert.Equal(16779521L, dict["latest_version"]);
    }

    [Fact]
    public void MapState_UpdateIdle_ExtractsState()
    {
        var json = """{"update":{"state":"idle","installed_version":16779265}}""";
        var known = new List<CapabilityDescriptor>
        {
            new() { Key = "update", Label = "Update", Unit = UnitType.Text },
        };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        var updateEntry = Assert.Single(updates, u => u.CapabilityKey == "update");
        var dict = Assert.IsType<Dictionary<string, object>>(updateEntry.Value);
        Assert.Equal("idle", dict["state"]);
    }

    [Fact]
    public void MapState_UpdateParsed_EvenWhenNotInCapabilities()
    {
        var json = """{"update":{"state":"available"},"temperature":22.0}""";
        var known = new List<CapabilityDescriptor>
        {
            new() { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius },
        };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.Contains(updates, u => u.CapabilityKey == "update");
    }

    [Fact]
    public void MapState_UnmappedProperties_AreIgnored()
    {
        var json = """{"update":{"state":"available"},"linkquality":50}""";
        var known = new List<CapabilityDescriptor>
        {
            new() { Key = "update", Label = "Update", Unit = UnitType.Text },
        };
        var updates = Zigbee2MqttStateMapper.MapState(json, known);
        Assert.DoesNotContain(updates, u => u.CapabilityKey == "linkquality");
    }
}
