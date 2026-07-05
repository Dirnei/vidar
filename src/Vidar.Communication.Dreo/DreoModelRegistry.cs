namespace Vidar.Communication.Dreo;

public static class DreoModelRegistry
{
    // Default fan-speed ceiling until a device reports its real range at onboarding/E2E.
    private const int DefaultMaxSpeed = 6;

    // Every Dreo ceiling fan currently resolves to the same capability set. The model
    // string is retained so per-model overrides (speed range, no color-temp, etc.) can
    // be added later without changing the call sites.
    public static DreoModelProfile Resolve(string model) =>
        new(model, CeilingFanCapabilities().ToList());

    public static IEnumerable<DreoCapability> CeilingFanCapabilities() => new[]
    {
        new DreoCapability("power", "Power", "OnOff", true),
        new DreoCapability("fan", "Fan", "OnOff", true),
        new DreoCapability("fan_speed", "Fan Speed", "Number", true, 1, DefaultMaxSpeed),
        new DreoCapability("mode", "Mode", "Text", true),
        new DreoCapability("direction", "Direction", "Text", true),
        new DreoCapability("light", "Light", "OnOff", true),
        new DreoCapability("light_brightness", "Brightness", "Percent", true, 0, 100),
        new DreoCapability("light_color_temp", "Color Temperature", "Number", true, 0, 100),
    };
}
