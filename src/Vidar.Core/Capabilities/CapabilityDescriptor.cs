namespace Vidar.Core.Capabilities;

// A selectable choice for an enumerated capability (e.g. a fan's mode: 1=Straight, 2=Natural).
// The Value is the raw value sent as the command / reported as state; Label is for display.
public sealed record CapabilityOption(double Value, string Label);

public sealed class CapabilityDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public UnitType Unit { get; init; }
    public bool Commandable { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    // When set, the capability is an enumerated choice: the UI renders a labeled picker over
    // these options instead of a slider/free input. The value still follows Unit (e.g. Number).
    public IReadOnlyList<CapabilityOption>? Options { get; init; }

    public ValueKind ValueKind => Units.KindOf(Unit);
}
