namespace Vidar.Core.Model;

public sealed class ThresholdEventLog
{
    public Guid Id { get; init; }
    public Guid RuleId { get; init; }
    public required string RuleName { get; init; }
    public required string EventName { get; init; }
    public Guid DeviceId { get; init; }
    public required string CapabilityKey { get; init; }
    public double CurrentValue { get; init; }
    public double ThresholdValue { get; init; }
    public string? StringValue { get; init; }
    public ThresholdOperator Operator { get; init; }
    public DateTimeOffset FiredAt { get; init; }
}
