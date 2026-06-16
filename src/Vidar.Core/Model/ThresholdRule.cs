using Vidar.Core.Capabilities;

namespace Vidar.Core.Model;

public enum ThresholdOperator
{
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    CrossesAbove,
    CrossesBelow
}

public sealed class ThresholdRule
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public Guid DeviceId { get; init; }
    public CapabilityType Capability { get; init; }
    public string? MetricKey { get; init; }
    public ThresholdOperator Operator { get; init; }
    public double Value { get; init; }
    public required string EventName { get; set; }
    public bool Enabled { get; set; } = true;
}
