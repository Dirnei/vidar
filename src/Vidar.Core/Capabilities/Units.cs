using System.Globalization;

namespace Vidar.Core.Capabilities;

public static class Units
{
    public static ValueKind KindOf(UnitType unit) => unit switch
    {
        UnitType.OnOff or UnitType.OpenClosed or UnitType.Detected or UnitType.YesNo => ValueKind.Boolean,
        // An Action carries no state; treat its command value as boolean (a momentary trigger).
        UnitType.Action => ValueKind.Boolean,
        UnitType.Text or UnitType.Url => ValueKind.String,
        _ => ValueKind.Numeric,
    };

    public static string Symbol(UnitType unit) => unit switch
    {
        UnitType.Watts => "W",
        UnitType.Kilowatts => "kW",
        UnitType.WattHours => "Wh",
        UnitType.KilowattHours => "kWh",
        UnitType.Celsius => "°C",
        UnitType.Fahrenheit => "°F",
        UnitType.Percent => "%",
        UnitType.Lux => "lx",
        UnitType.Hectopascals => "hPa",
        UnitType.KilometersPerHour => "km/h",
        UnitType.Millimeters => "mm",
        UnitType.Degrees => "°",
        UnitType.WattsPerSquareMeter => "W/m²",
        UnitType.Minutes => "min",
        UnitType.UvIndex => "",
        _ => "",
    };

    public static (string TrueLabel, string FalseLabel) BooleanLabels(UnitType unit) => unit switch
    {
        UnitType.OnOff => ("On", "Off"),
        UnitType.OpenClosed => ("Open", "Closed"),
        UnitType.Detected => ("Detected", "Not detected"),
        UnitType.YesNo => ("Yes", "No"),
        _ => ("True", "False"),
    };

    public static double ToBase(double value, UnitType unit) => unit switch
    {
        UnitType.Kilowatts => value * 1000,
        UnitType.KilowattHours => value * 1000,
        UnitType.Fahrenheit => (value - 32) * 5.0 / 9.0,
        _ => value,
    };

    public static UnitType BaseOf(UnitType unit) => unit switch
    {
        UnitType.Kilowatts => UnitType.Watts,
        UnitType.KilowattHours => UnitType.WattHours,
        UnitType.Fahrenheit => UnitType.Celsius,
        _ => unit,
    };

    public static string FormatNumeric(double value, UnitType unit)
    {
        var sym = Symbol(unit);
        return unit switch
        {
            UnitType.Watts when Math.Abs(value) >= 1000 =>
                string.Create(CultureInfo.InvariantCulture, $"{value / 1000:F1} kW"),
            UnitType.WattHours when Math.Abs(value) >= 1000 =>
                string.Create(CultureInfo.InvariantCulture, $"{value / 1000:F1} kWh"),
            UnitType.Percent =>
                string.Create(CultureInfo.InvariantCulture, $"{value:F0}%"),
            UnitType.Degrees =>
                string.Create(CultureInfo.InvariantCulture, $"{value:G}°"),
            _ when sym.Length > 0 =>
                string.Create(CultureInfo.InvariantCulture, $"{value:G} {sym}"),
            _ => value.ToString("G", CultureInfo.InvariantCulture),
        };
    }

    public static string FormatBoolean(bool value, UnitType unit)
    {
        var (t, f) = BooleanLabels(unit);
        return value ? t : f;
    }
}
