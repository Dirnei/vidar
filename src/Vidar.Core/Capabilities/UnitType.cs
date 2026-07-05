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

    // Numeric — Duration
    Minutes,

    // Boolean
    OnOff,
    OpenClosed,
    Detected,
    YesNo,

    // String
    Text,
    Url,

    // Action — a momentary command with no persistent state (renders as a button, not a toggle).
    // e.g. a vacuum's start / stop / dock / locate.
    Action,
}
