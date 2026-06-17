using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

public sealed class ThresholdEvaluatorActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _mediator;
    private readonly IThresholdEventLogRepository _eventLogRepo;
    private readonly List<ThresholdRule> _rules = [];
    private readonly Dictionary<(Guid DeviceId, string CapabilityKey), object> _previousValues = new();

    public static Props Props(IThresholdRuleRepository ruleRepo, IThresholdEventLogRepository eventLogRepo) =>
        Akka.Actor.Props.Create(() => new ThresholdEvaluatorActor(ruleRepo, eventLogRepo));

    public ThresholdEvaluatorActor(IThresholdRuleRepository ruleRepo, IThresholdEventLogRepository eventLogRepo)
    {
        _eventLogRepo = eventLogRepo;
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<DeviceStateChanged>(OnStateChanged);

        Receive<AddThresholdRule>(msg =>
        {
            _rules.Add(msg.Rule);
            _ = ruleRepo.UpsertAsync(msg.Rule);
        });

        Receive<UpdateThresholdRule>(msg =>
        {
            _rules.RemoveAll(r => r.Id == msg.Rule.Id);
            _rules.Add(msg.Rule);
            _ = ruleRepo.UpsertAsync(msg.Rule);
        });

        Receive<RemoveThresholdRule>(msg =>
        {
            _rules.RemoveAll(r => r.Id == msg.RuleId);
            _ = ruleRepo.DeleteAsync(msg.RuleId);
        });

        Receive<GetAllThresholdRules>(_ =>
        {
            Sender.Tell(new AllThresholdRulesResponse(new List<ThresholdRule>(_rules)));
        });

        Receive<RulesLoaded>(msg =>
        {
            _rules.AddRange(msg.Rules);
            _log.Info("Loaded {Count} threshold rules", msg.Rules.Count);
        });

        _ = LoadRulesAsync(ruleRepo);
    }

    protected override void PreStart()
    {
        base.PreStart();
        _mediator.Tell(new Subscribe("device-state-changes", Self));
    }

    private async Task LoadRulesAsync(IThresholdRuleRepository ruleRepo)
    {
        var rules = await ruleRepo.GetAllAsync();
        Self.Tell(new RulesLoaded(rules));
    }

    private void OnStateChanged(DeviceStateChanged msg)
    {
        foreach (var rule in _rules)
        {
            if (!rule.Enabled) continue;
            if (rule.DeviceId != msg.DeviceId) continue;
            if (rule.CapabilityKey != msg.CapabilityKey) continue;

            var key = (msg.DeviceId, msg.CapabilityKey);
            _previousValues.TryGetValue(key, out var previousValue);
            var hasPrevious = _previousValues.ContainsKey(key);

            if (!Evaluate(rule, msg.Value, previousValue, hasPrevious, out var currentNumeric))
            {
                _previousValues[key] = msg.Value;
                continue;
            }

            var evt = new ThresholdEvent(
                rule.Id, rule.Name, rule.EventName,
                msg.DeviceId, msg.CapabilityKey,
                currentNumeric, rule.Value, rule.Operator,
                DateTimeOffset.UtcNow);

            _mediator.Tell(new Publish("threshold-events", evt));

            var logEntry = new ThresholdEventLog
            {
                Id = Guid.NewGuid(),
                RuleId = rule.Id,
                RuleName = rule.Name,
                EventName = rule.EventName,
                DeviceId = msg.DeviceId,
                CapabilityKey = msg.CapabilityKey,
                CurrentValue = currentNumeric,
                ThresholdValue = rule.Value,
                StringValue = rule.StringValue,
                Operator = rule.Operator,
                FiredAt = evt.FiredAt
            };
            _ = _eventLogRepo.InsertAsync(logEntry);

            _log.Info("Threshold event fired: {EventName} (rule={RuleName}, value={Value}, threshold={Threshold})",
                evt.EventName, evt.RuleName, currentNumeric, rule.Value);

            _previousValues[key] = msg.Value;
        }
    }

    private static bool Evaluate(ThresholdRule rule, object currentValue, object? previousValue, bool hasPrevious, out double currentNumeric)
    {
        currentNumeric = 0;

        return rule.Operator switch
        {
            // Numeric operators
            ThresholdOperator.GreaterThan or
            ThresholdOperator.LessThan or
            ThresholdOperator.GreaterThanOrEqual or
            ThresholdOperator.LessThanOrEqual or
            ThresholdOperator.CrossesAbove or
            ThresholdOperator.CrossesBelow =>
                TryConvertToDouble(currentValue, out currentNumeric) &&
                EvaluateNumeric(rule.Operator, currentNumeric, rule.Value,
                    hasPrevious && TryConvertToDouble(previousValue!, out var prev) ? prev : 0.0, hasPrevious),

            // Boolean operators
            ThresholdOperator.BecomesTrue => EvaluateBoolean(currentValue, previousValue, hasPrevious, true),
            ThresholdOperator.BecomesFalse => EvaluateBoolean(currentValue, previousValue, hasPrevious, false),
            ThresholdOperator.Changes => EvaluateChanges(currentValue, previousValue, hasPrevious),

            // String operators
            ThresholdOperator.Equals => EvaluateStringEquals(currentValue, rule.StringValue, negate: false),
            ThresholdOperator.NotEquals => EvaluateStringEquals(currentValue, rule.StringValue, negate: true),

            _ => false
        };
    }

    private static bool EvaluateNumeric(ThresholdOperator op, double current, double threshold, double previous, bool hasPrevious)
    {
        return op switch
        {
            ThresholdOperator.GreaterThan => current > threshold,
            ThresholdOperator.LessThan => current < threshold,
            ThresholdOperator.GreaterThanOrEqual => current >= threshold,
            ThresholdOperator.LessThanOrEqual => current <= threshold,
            ThresholdOperator.CrossesAbove => hasPrevious && previous <= threshold && current > threshold,
            ThresholdOperator.CrossesBelow => hasPrevious && previous >= threshold && current < threshold,
            _ => false
        };
    }

    private static bool EvaluateBoolean(object currentValue, object? previousValue, bool hasPrevious, bool targetState)
    {
        if (!TryConvertToBool(currentValue, out var current)) return false;
        if (!hasPrevious) return current == targetState;
        if (!TryConvertToBool(previousValue!, out var previous)) return current == targetState;
        return current == targetState && previous != targetState;
    }

    private static bool EvaluateChanges(object currentValue, object? previousValue, bool hasPrevious)
    {
        if (!hasPrevious) return false;
        return !System.Collections.Generic.EqualityComparer<object>.Default.Equals(currentValue, previousValue);
    }

    private static bool EvaluateStringEquals(object currentValue, string? expected, bool negate)
    {
        var current = currentValue?.ToString();
        var match = string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
        return negate ? !match : match;
    }

    private static bool TryConvertToDouble(object value, out double result)
    {
        result = 0;
        try
        {
            result = Convert.ToDouble(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertToBool(object value, out bool result)
    {
        result = false;
        if (value is bool b) { result = b; return true; }
        if (value is string s && bool.TryParse(s, out var parsed)) { result = parsed; return true; }
        if (TryConvertToDouble(value, out var d)) { result = d != 0; return true; }
        return false;
    }

    private sealed record RulesLoaded(List<ThresholdRule> Rules);
}
