using TurboHomeConnect.Model;
using Vidar.Communication.HomeConnect;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.HomeConnect.Tests;

public class HomeConnectApplianceMapperTests
{
    [Fact]
    public void Map_Dishwasher_ReturnsDiscoveredDeviceWithExtrasAndSwitch()
    {
        var appliance = new HomeAppliance(
            HaId: "BOSCH-HCS06COM1-68A40E27EC5A",
            Vib: "SMV6ZCX49E",
            Brand: "Bosch",
            Type: "Dishwasher",
            Name: "Dishwasher Kitchen",
            ENumber: "SMV6ZCX49E/14",
            Connected: true);

        var result = HomeConnectApplianceMapper.Map(appliance);

        Assert.Equal("homeconnect", result.CommunicationType);
        Assert.Equal("BOSCH-HCS06COM1-68A40E27EC5A", result.NativeId);
        Assert.Contains(CapabilityType.Switch, result.Capabilities);
        Assert.Contains(CapabilityType.Extras, result.Capabilities);
        Assert.Equal("Bosch", result.Metadata["brand"]);
        Assert.Equal("Dishwasher", result.Metadata["type"]);
        Assert.Equal("Dishwasher Kitchen", result.Metadata["name"]);
        Assert.Equal("SMV6ZCX49E/14", result.Metadata["enumber"]);
    }

    [Fact]
    public void Map_NullOptionalFields_OmitsFromMetadata()
    {
        var appliance = new HomeAppliance(
            HaId: "SIEMENS-HCS06COM1-1234",
            Vib: null,
            Brand: null,
            Type: "Oven",
            Name: null,
            ENumber: null,
            Connected: false);

        var result = HomeConnectApplianceMapper.Map(appliance);

        Assert.Equal("Oven", result.Metadata["type"]);
        Assert.DoesNotContain("brand", result.Metadata.Keys);
        Assert.DoesNotContain("name", result.Metadata.Keys);
        Assert.DoesNotContain("enumber", result.Metadata.Keys);
    }
}
