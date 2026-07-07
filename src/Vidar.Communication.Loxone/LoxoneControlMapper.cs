using Vidar.Core.Capabilities;

namespace Vidar.Communication.Loxone;

// Maps a Loxone control to Vidar capabilities. Present-fields: unsupported types return [] and
// the caller skips them. Phase A types only — RGBW/tunable-white/climate are Phase B.
public static class LoxoneControlMapper
{
    public static List<CapabilityDescriptor> Map(LoxoneControl c) => c.Type switch
    {
        "Switch" or "Pushbutton" =>
        [
            new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.OnOff, Commandable = true },
        ],

        // Composite light card: Unit OnOff, value {on, brightness}.
        "Dimmer" =>
        [
            new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true },
        ],

        "LightControllerV2" =>
        [
            new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.OnOff, Commandable = true },
            new CapabilityDescriptor
            {
                Key = "mode", Label = "Scene", Unit = UnitType.Number, Commandable = true,
                Options = c.Moods.Select(m => new CapabilityOption(m.Id, m.Name)).ToList(),
            },
        ],

        "PresenceDetector" =>
        [
            new CapabilityDescriptor { Key = "presence", Label = "Presence", Unit = UnitType.Detected },
            new CapabilityDescriptor { Key = "brightness", Label = "Brightness", Unit = UnitType.Lux },
        ],

        "SmokeAlarm" =>
        [
            new CapabilityDescriptor { Key = "smoke", Label = "Smoke", Unit = UnitType.Detected },
            new CapabilityDescriptor { Key = "battery", Label = "Battery", Unit = UnitType.Percent, Min = 0, Max = 100 },
            new CapabilityDescriptor { Key = "tamper", Label = "Tamper", Unit = UnitType.Detected },
        ],

        "Touch" =>
        [
            new CapabilityDescriptor { Key = "action", Label = "Action", Unit = UnitType.Text },
        ],

        _ => [],
    };
}
