using Vidar.Core.Capabilities;

namespace Vidar.Communication.Dreo;

public static class DreoModelRegistry
{
    // DR-HCF001S reports a 1..12 wind range (controlsConf Speed control).
    private const int DefaultMaxSpeed = 12;

    // Fan `mode` is an enum (controlsConf CFFan control, cmd "mode").
    private static readonly CapabilityOption[] ModeOptions =
    {
        new(1, "Straight"),
        new(2, "Natural"),
        new(3, "Sleep"),
        new(4, "Reverse"),
    };

    // Every Dreo ceiling fan currently resolves to the same capability set. The model
    // string is retained so per-model overrides (speed range, no color-temp, etc.) can
    // be added later without changing the call sites.
    public static DreoModelProfile Resolve(string model) =>
        new(model, CeilingFanCapabilities().ToList());

    public static IEnumerable<DreoCapability> CeilingFanCapabilities() => new[]
    {
        // No master-power capability: `mcuon` is a read-only state field and the device rejects
        // it as a command ("instruction validate failed"). Fan and light are the real controls.
        new DreoCapability("fan", "Fan", "OnOff", true),
        new DreoCapability("fan_speed", "Fan Speed", "Number", true, 1, DefaultMaxSpeed),
        // mode is an enum; value 4 (Reverse) is this model's summer/winter direction.
        new DreoCapability("mode", "Mode", "Number", true, Options: ModeOptions),
        // `light` is a COMPOSITE capability (Unit OnOff, value {on, brightness}) so the frontend
        // renders the unified z2m-style light+brightness card. Color temp stays a separate slider
        // (that card expects mireds; Dreo's colortemp scale differs).
        new DreoCapability("light", "Light", "OnOff", true),
        new DreoCapability("light_color_temp", "Color Temperature", "Number", true, 0, 100),
    };
}
