namespace Vidar.Core.Capabilities;

public enum UnitType
{
    // Numeric — Power
    Watts,
    Kilowatts,

    // Numeric — Energy
    WattHours,
    KilowattHours,

    // Numeric — Temperature
    Celsius,
    Fahrenheit,

    // Numeric — Ratio
    Percent,

    // Numeric — Light
    Lux,

    // Numeric — Generic
    Number,

    // Numeric — Weather
    Hectopascals,
    KilometersPerHour,
    Millimeters,
    Degrees,
    UvIndex,
    WattsPerSquareMeter,

    // Boolean
    OnOff,
    OpenClosed,
    Detected,
    YesNo,

    // String
    Text,
    Url,
}
