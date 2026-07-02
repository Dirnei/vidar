using System.Globalization;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.Ecowitt;

/// <summary>
/// Pure translation of an Ecowitt "customized" MQTT payload (a flat
/// <c>key=value&amp;…</c> URL-parameter blob, always imperial) into Vidar
/// capabilities and metric state updates. A capability/update is produced only
/// for a field that is actually present, so the output self-adapts to whichever
/// sub-sensors the gateway reports.
/// </summary>
public static class EcowittStateMapper
{
    private static double FahrenheitToCelsius(double f) => (f - 32.0) * 5.0 / 9.0;
    private static double InHgToHectopascals(double v) => v * 33.8638866667;
    private static double MphToKmh(double v) => v * 1.609344;
    private static double InToMm(double v) => v * 25.4;
    private static double Identity(double v) => v;

    private sealed record NumericField(
        string Field, string Capability, string Label, UnitType Unit,
        Func<double, double> Convert, double? Min = null, double? Max = null);

    // Order defines capability emission order. Where two field names map to the
    // same capability (classic vs. WS90 piezo rain), whichever is present wins;
    // if both are present the first listed wins (dedup by capability key).
    private static readonly NumericField[] NumericFields =
    [
        new("tempf", "outdoorTemperature", "Outdoor Temperature", UnitType.Celsius, FahrenheitToCelsius),
        new("humidity", "outdoorHumidity", "Outdoor Humidity", UnitType.Percent, Identity, 0, 100),
        new("tempinf", "indoorTemperature", "Indoor Temperature", UnitType.Celsius, FahrenheitToCelsius),
        new("humidityin", "indoorHumidity", "Indoor Humidity", UnitType.Percent, Identity, 0, 100),
        new("baromrelin", "pressure", "Pressure (relative)", UnitType.Hectopascals, InHgToHectopascals),
        new("baromabsin", "pressureAbsolute", "Pressure (absolute)", UnitType.Hectopascals, InHgToHectopascals),
        new("vpd", "vaporPressureDeficit", "Vapor Pressure Deficit", UnitType.Hectopascals, InHgToHectopascals),
        new("winddir", "windDirection", "Wind Direction", UnitType.Degrees, Identity, 0, 360),
        new("windspeedmph", "windSpeed", "Wind Speed", UnitType.KilometersPerHour, MphToKmh),
        new("windgustmph", "windGust", "Wind Gust", UnitType.KilometersPerHour, MphToKmh),
        new("maxdailygust", "windGustMax", "Max Daily Gust", UnitType.KilometersPerHour, MphToKmh),
        new("solarradiation", "solarRadiation", "Solar Radiation", UnitType.WattsPerSquareMeter, Identity),
        new("uv", "uvIndex", "UV Index", UnitType.UvIndex, Identity, 0),
        new("rainratein", "rainRate", "Rain Rate", UnitType.Millimeters, InToMm),
        new("rrain_piezo", "rainRate", "Rain Rate", UnitType.Millimeters, InToMm),
        new("eventrainin", "eventRain", "Event Rain", UnitType.Millimeters, InToMm),
        new("hourlyrainin", "hourlyRain", "Hourly Rain", UnitType.Millimeters, InToMm),
        new("dailyrainin", "dailyRain", "Daily Rain", UnitType.Millimeters, InToMm),
        new("drain_piezo", "dailyRain", "Daily Rain", UnitType.Millimeters, InToMm),
        new("weeklyrainin", "weeklyRain", "Weekly Rain", UnitType.Millimeters, InToMm),
        new("monthlyrainin", "monthlyRain", "Monthly Rain", UnitType.Millimeters, InToMm),
        new("yearlyrainin", "yearlyRain", "Yearly Rain", UnitType.Millimeters, InToMm),
    ];

    // Battery flags: Ecowitt reports 0 = OK, 1 = low for the WH65/WS65 array.
    private static readonly (string Field, string Capability, string Label)[] BatteryFields =
    [
        ("wh65batt", "outdoorSensorBatteryLow", "Outdoor Sensor Battery Low"),
    ];

    public static Dictionary<string, string> ParsePayload(string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        foreach (var pair in payload.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
                continue;
            var key = pair[..idx];
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }

    public static string? TryGetPassKey(IReadOnlyDictionary<string, string> fields) =>
        fields.TryGetValue("PASSKEY", out var p) && !string.IsNullOrWhiteSpace(p) ? p : null;

    /// <summary>
    /// Resolves each numeric capability to the first listed field that is both
    /// present and parses as a number. This is the single source of truth for
    /// "which numeric fields count" so that <see cref="Map"/> and
    /// <see cref="BuildCapabilities"/> can never disagree.
    /// </summary>
    private static IEnumerable<(NumericField Field, double Value)> ResolveNumericFields(
        IReadOnlyDictionary<string, string> fields)
    {
        var seen = new HashSet<string>();

        foreach (var f in NumericFields)
        {
            if (seen.Contains(f.Capability))
                continue;
            if (!fields.TryGetValue(f.Field, out var raw))
                continue;
            if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out var num))
                continue;
            seen.Add(f.Capability);
            yield return (f, num);
        }
    }

    public static List<DeviceStateUpdate> Map(Guid deviceId, IReadOnlyDictionary<string, string> fields)
    {
        var updates = new List<DeviceStateUpdate>();

        foreach (var (f, num) in ResolveNumericFields(fields))
            updates.Add(new DeviceStateUpdate(deviceId, f.Capability, f.Convert(num)));

        foreach (var (field, capability, _) in BatteryFields)
        {
            if (!fields.TryGetValue(field, out var raw))
                continue;
            updates.Add(new DeviceStateUpdate(deviceId, capability, raw != "0"));
        }

        return updates;
    }

    public static List<CapabilityDescriptor> BuildCapabilities(IReadOnlyDictionary<string, string> fields)
    {
        var caps = new List<CapabilityDescriptor>();

        foreach (var (f, _) in ResolveNumericFields(fields))
        {
            caps.Add(new CapabilityDescriptor
            {
                Key = f.Capability,
                Label = f.Label,
                Unit = f.Unit,
                Min = f.Min,
                Max = f.Max,
            });
        }

        foreach (var (field, capability, label) in BatteryFields)
        {
            if (!fields.ContainsKey(field))
                continue;
            caps.Add(new CapabilityDescriptor { Key = capability, Label = label, Unit = UnitType.YesNo });
        }

        return caps;
    }

    public static Dictionary<string, string> BuildMetadata(IReadOnlyDictionary<string, string> fields) => new()
    {
        ["manufacturer"] = "Ecowitt",
        ["model"] = "GW3001",
        ["stationtype"] = fields.GetValueOrDefault("stationtype", ""),
    };
}
