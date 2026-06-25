namespace Vidar.Core.Model;

public enum ThresholdOperator
{
    // Numeric
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    CrossesAbove,
    CrossesBelow,
    // Boolean
    BecomesTrue,
    BecomesFalse,
    Changes,
    // String
    Equals,
    NotEquals,
}

public sealed class ThresholdRule
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public Guid DeviceId { get; init; }
    public required string CapabilityKey { get; init; }
    public ThresholdOperator Operator { get; init; }
    public double Value { get; init; }
    public string? StringValue { get; init; }
    public required string EventName { get; set; }
    public bool Enabled { get; set; } = true;
    public double? ResetValue { get; init; }
}
