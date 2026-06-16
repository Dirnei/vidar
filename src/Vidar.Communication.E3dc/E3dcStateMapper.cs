using E3dc;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.E3dc;

public static class E3dcStateMapper
{
    public static List<DeviceStateUpdate> MapSnapshot(Guid deviceId, EmsPowerSnapshot snapshot)
    {
        return
        [
            new DeviceStateUpdate(deviceId, CapabilityType.SolarProduction, snapshot.PvWatts),
            new DeviceStateUpdate(deviceId, CapabilityType.GridPower, snapshot.GridWatts),
            new DeviceStateUpdate(deviceId, CapabilityType.Consumption, snapshot.HomeWatts),
            new DeviceStateUpdate(deviceId, CapabilityType.Battery, (double)snapshot.Soc),
            new DeviceStateUpdate(deviceId, CapabilityType.Extras, new Dictionary<string, object>
            {
                ["batteryWatts"] = snapshot.BatteryWatts,
                ["additionalWatts"] = snapshot.AdditionalWatts,
                ["autarky"] = (double)snapshot.Autarky,
                ["selfConsumption"] = (double)snapshot.SelfConsumption,
            })
        ];
    }
}
