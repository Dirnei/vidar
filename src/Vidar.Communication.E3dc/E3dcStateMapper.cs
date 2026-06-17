using E3dc;
using Vidar.Core.Messages;

namespace Vidar.Communication.E3dc;

public static class E3dcStateMapper
{
    public static List<DeviceStateUpdate> MapSnapshot(Guid deviceId, EmsPowerSnapshot snapshot)
    {
        return
        [
            new DeviceStateUpdate(deviceId, "solarProduction", snapshot.PvWatts),
            new DeviceStateUpdate(deviceId, "gridPower", snapshot.GridWatts),
            new DeviceStateUpdate(deviceId, "consumption", snapshot.HomeWatts),
            new DeviceStateUpdate(deviceId, "batteryCharge", (double)snapshot.Soc),
            new DeviceStateUpdate(deviceId, "batteryPower", snapshot.BatteryWatts),
            new DeviceStateUpdate(deviceId, "additionalPower", snapshot.AdditionalWatts),
            new DeviceStateUpdate(deviceId, "autarky", (double)snapshot.Autarky),
            new DeviceStateUpdate(deviceId, "selfConsumption", (double)snapshot.SelfConsumption),
        ];
    }
}
