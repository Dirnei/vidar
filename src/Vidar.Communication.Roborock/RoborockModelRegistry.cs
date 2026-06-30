using Vidar.Core.Capabilities;

namespace Vidar.Communication.Roborock;

public static class RoborockModelRegistry
{
    private static CapabilityDescriptor Read(string key, string label, UnitType unit,
        double? min = null, double? max = null) =>
        new() { Key = key, Label = label, Unit = unit, Commandable = false, Min = min, Max = max };

    private static CapabilityDescriptor Cmd(string key, string label, UnitType unit,
        double? min = null, double? max = null) =>
        new() { Key = key, Label = label, Unit = unit, Commandable = true, Min = min, Max = max };

    // Default vacuum profile — covers the Qrevo S Pro and any python-roborock V1 device.
    private static readonly IReadOnlyList<CapabilityDescriptor> DefaultCapabilities = new[]
    {
        Read("vacuum.state", "State", UnitType.Text),
        Read("vacuum.battery", "Battery", UnitType.Percent, 0, 100),
        Cmd("vacuum.fanPower", "Suction", UnitType.Number, 101, 106),
        Cmd("vacuum.start", "Start", UnitType.OnOff),
        Cmd("vacuum.stop", "Stop", UnitType.OnOff),
        Cmd("vacuum.pause", "Pause", UnitType.OnOff),
        Cmd("vacuum.dock", "Return to dock", UnitType.OnOff),
        Cmd("vacuum.locate", "Locate", UnitType.OnOff),
        Cmd("vacuum.cleanSegments", "Clean rooms", UnitType.Text),
    };

    public static bool IsSupported(string model) => !string.IsNullOrWhiteSpace(model);

    public static RoborockModelProfile Resolve(string model) =>
        new(model, DefaultCapabilities);

    public static IReadOnlyList<CapabilityDescriptor> Capabilities(string model) =>
        Resolve(model).Capabilities;
}
