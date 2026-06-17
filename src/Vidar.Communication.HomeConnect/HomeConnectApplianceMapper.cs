using TurboHomeConnect.Model;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.HomeConnect;

public static class HomeConnectApplianceMapper
{
    public static DeviceDiscovered Map(HomeAppliance appliance)
    {
        var metadata = new Dictionary<string, string> { ["type"] = appliance.Type };

        if (appliance.Brand is not null)
            metadata["brand"] = appliance.Brand;
        if (appliance.Name is not null)
            metadata["name"] = appliance.Name;
        if (appliance.ENumber is not null)
            metadata["enumber"] = appliance.ENumber;
        if (appliance.Vib is not null)
            metadata["vib"] = appliance.Vib;

        var capabilities = new List<CapabilityDescriptor>
        {
            new() { Key = "switch", Label = "Power", Unit = UnitType.OnOff, Commandable = true },
            new() { Key = "operationState", Label = "Operation State", Unit = UnitType.Text },
            new() { Key = "remainingTime", Label = "Remaining Time", Unit = UnitType.Number },
            new() { Key = "progress", Label = "Progress", Unit = UnitType.Percent },
        };

        return new DeviceDiscovered(
            DeviceId: Guid.NewGuid(),
            CommunicationType: "homeconnect",
            NativeId: appliance.HaId,
            Capabilities: capabilities,
            Metadata: metadata);
    }
}
