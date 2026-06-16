using Vidar.Core.Capabilities;
using Vidar.Core.Model;

namespace Vidar.Core.Messages;

public sealed record ThresholdEvent(
    Guid RuleId,
    string RuleName,
    string EventName,
    Guid DeviceId,
    CapabilityType Capability,
    string? MetricKey,
    double CurrentValue,
    double ThresholdValue,
    ThresholdOperator Operator,
    DateTimeOffset FiredAt);
