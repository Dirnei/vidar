namespace Vidar.Core.Capabilities;

public sealed class CapabilityDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public UnitType Unit { get; init; }
    public bool Commandable { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    public ValueKind ValueKind => Units.KindOf(Unit);
}
