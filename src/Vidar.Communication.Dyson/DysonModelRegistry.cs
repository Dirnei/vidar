namespace Vidar.Communication.Dyson;

public static class DysonModelRegistry
{
    private static readonly HashSet<string> RobotCodes = new(StringComparer.OrdinalIgnoreCase)
        { "N223", "276", "277" };

    // Hot+Cool families (variants share the leading digits)
    private static readonly HashSet<string> HotCoolCodes = new(StringComparer.OrdinalIgnoreCase)
        { "455", "527" };

    private static readonly HashSet<string> HumidifyCoolCodes = new(StringComparer.OrdinalIgnoreCase)
        { "358" };

    public static bool IsSupported(string productType) =>
        !RobotCodes.Contains(Normalize(productType));

    public static DysonModelProfile Resolve(string productType)
    {
        var baseCode = Normalize(productType);
        var feature =
            HumidifyCoolCodes.Contains(baseCode) ? DysonFeature.HumidifyCool :
            HotCoolCodes.Contains(baseCode) ? DysonFeature.HotCool :
            DysonFeature.Base;

        var caps = new List<DysonCapability>(BaseCapabilities());
        if (feature == DysonFeature.HotCool) caps.AddRange(HotCoolCapabilities());
        if (feature == DysonFeature.HumidifyCool) caps.AddRange(HumidifyCapabilities());

        return new DysonModelProfile(productType, feature, caps);
    }

    // Strip the K/E/M suffix so "358K" resolves like "358".
    private static string Normalize(string productType)
    {
        var t = productType.Trim();
        if (t.Length > 0 && (t[^1] is 'K' or 'E' or 'M' or 'k' or 'e' or 'm'))
        {
            var head = t[..^1];
            if (head.All(char.IsDigit)) return head;
        }
        return t;
    }

    private static IEnumerable<DysonCapability> BaseCapabilities() => new[]
    {
        new DysonCapability("power", "Power", "OnOff", true),
        new DysonCapability("fan_speed", "Fan Speed", "Number", true, 0, 10),
        new DysonCapability("auto", "Auto Mode", "OnOff", true),
        new DysonCapability("night_mode", "Night Mode", "OnOff", true),
        new DysonCapability("oscillation", "Oscillation", "OnOff", true),
        new DysonCapability("continuous_monitoring", "Continuous Monitoring", "OnOff", true),
        new DysonCapability("sleep_timer", "Sleep Timer", "Number", false),
        new DysonCapability("hepa_filter", "HEPA Filter Life", "Percent", false),
        new DysonCapability("carbon_filter", "Carbon Filter Life", "Percent", false),
        new DysonCapability("pm25", "PM2.5", "Number", false),
        new DysonCapability("pm10", "PM10", "Number", false),
        new DysonCapability("voc", "VOC", "Number", false),
        new DysonCapability("no2", "NO2", "Number", false),
        new DysonCapability("temperature", "Temperature", "Celsius", false),
        new DysonCapability("humidity", "Humidity", "Percent", false),
    };

    private static IEnumerable<DysonCapability> HotCoolCapabilities() => new[]
    {
        new DysonCapability("heat", "Heating", "OnOff", true),
        new DysonCapability("target_temperature", "Target Temperature", "Celsius", true, 1, 37),
    };

    private static IEnumerable<DysonCapability> HumidifyCapabilities() => new[]
    {
        new DysonCapability("humidify", "Humidify", "OnOff", true),
        new DysonCapability("auto_humidify", "Auto Humidify", "OnOff", true),
        new DysonCapability("target_humidity", "Target Humidity", "Percent", true, 30, 70),
    };
}
