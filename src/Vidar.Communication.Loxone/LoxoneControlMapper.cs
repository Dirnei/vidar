using Vidar.Core.Capabilities;

namespace Vidar.Communication.Loxone;

// Maps a Loxone control to Vidar capabilities. Present-fields: unsupported types return [] and
// the caller skips them.
public static class LoxoneControlMapper
{
    public static List<CapabilityDescriptor> Map(LoxoneControl c) => c.Type switch
    {
        // Generic on/off relay (not necessarily lighting) -> `switch`, matching Shelly/Zigbee.
        "Switch" or "Pushbutton" =>
        [
            new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true },
        ],

        // Composite light card: Unit OnOff, value {on, brightness}.
        "Dimmer" =>
        [
            new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true },
        ],

        // A lighting controller: on/off master (no brightness of its own) -> `light`, plus a scene
        // (mood) picker. `light` (not `power`) because it drives a lighting load, like a Dimmer.
        "LightControllerV2" =>
        [
            new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true },
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

        // RGBW dimmer (sidecar-normalized from ColorPickerV2 in RGBW mode): composite light +
        // an RGB color (hex) + a separate white channel.
        "ColorPickerRGBW" =>
        [
            new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true },
            new CapabilityDescriptor { Key = "light_color", Label = "Color", Unit = UnitType.Text, Commandable = true },
            new CapabilityDescriptor { Key = "light_white", Label = "White", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 },
        ],

        // Tunable-white dimmer (sidecar-normalized): composite light + color temperature (Kelvin).
        "ColorPickerTunableWhite" =>
        [
            new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true },
            new CapabilityDescriptor { Key = "light_color_temp", Label = "Color Temperature", Unit = UnitType.Number, Commandable = true, Min = 2700, Max = 6500 },
        ],

        // Intelligent Room Controller + valve actuators ("Stellmotoren"). climate_mode is a
        // distinct key (NOT `mode`) so its command doesn't collide with LightControllerV2 moods.
        "RoomControllerV2" =>
        [
            new CapabilityDescriptor { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius },
            new CapabilityDescriptor { Key = "target_temp", Label = "Target", Unit = UnitType.Celsius, Commandable = true, Min = 5, Max = 30 },
            new CapabilityDescriptor
            {
                Key = "climate_mode", Label = "Mode", Unit = UnitType.Number, Commandable = true,
                Options = [new CapabilityOption(0, "Automatic"), new CapabilityOption(1, "Comfort"), new CapabilityOption(2, "Economy"), new CapabilityOption(3, "Off")],
            },
            new CapabilityDescriptor { Key = "valve", Label = "Valve", Unit = UnitType.Percent, Min = 0, Max = 100 },
        ],

        _ => [],
    };
}
