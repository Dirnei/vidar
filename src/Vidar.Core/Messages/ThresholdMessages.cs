using Vidar.Core.Model;

namespace Vidar.Core.Messages;

public sealed record AddThresholdRule(ThresholdRule Rule);
public sealed record UpdateThresholdRule(ThresholdRule Rule);
public sealed record RemoveThresholdRule(Guid RuleId);
public sealed record GetAllThresholdRules;
public sealed record AllThresholdRulesResponse(List<ThresholdRule> Rules);
