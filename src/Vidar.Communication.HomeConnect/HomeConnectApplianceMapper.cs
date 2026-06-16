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

        return new DeviceDiscovered(
            DeviceId: Guid.NewGuid(),
            CommunicationType: "homeconnect",
            NativeId: appliance.HaId,
            Capabilities: [CapabilityType.Switch, CapabilityType.Extras],
            Metadata: metadata);
    }
}
