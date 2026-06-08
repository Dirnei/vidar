namespace Vidar.Core.Capabilities;

public static class CapabilityMetadata
{
    private static readonly Dictionary<CapabilityType, (bool Controllable, string Unit)> Metadata = new()
    {
        [CapabilityType.Switch] = (true, ""),
        [CapabilityType.Dimmer] = (true, "%"),
        [CapabilityType.Light] = (true, "%"),
        [CapabilityType.Cover] = (true, "%"),
        [CapabilityType.Temperature] = (false, "°C"),
        [CapabilityType.Motion] = (false, ""),
        [CapabilityType.Power] = (false, "W"),
        [CapabilityType.Energy] = (false, "kWh"),
        [CapabilityType.Humidity] = (false, "%"),
        [CapabilityType.Contact] = (false, ""),
        [CapabilityType.Action] = (false, ""),
        [CapabilityType.Battery] = (false, "%"),
        [CapabilityType.Extras] = (false, ""),
    };

    public static bool IsControllable(CapabilityType type) => Metadata[type].Controllable;
    public static string GetUnit(CapabilityType type) => Metadata[type].Unit;
}
