using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

public sealed class ThresholdEvaluatorActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _mediator;
    private readonly List<ThresholdRule> _rules = [];
    private readonly Dictionary<(Guid DeviceId, CapabilityType Capability, string? MetricKey), double> _previousValues = new();

    public static Props Props(IThresholdRuleRepository ruleRepo) =>
        Akka.Actor.Props.Create(() => new ThresholdEvaluatorActor(ruleRepo));

    public ThresholdEvaluatorActor(IThresholdRuleRepository ruleRepo)
    {
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
            if (rule.Capability != msg.Capability) continue;

            if (!TryExtractNumericValue(msg.Value, rule.MetricKey, out var currentValue))
                continue;

            var key = (msg.DeviceId, msg.Capability, rule.MetricKey);
            _previousValues.TryGetValue(key, out var previousValue);

            if (ShouldFire(rule.Operator, currentValue, rule.Value, previousValue, _previousValues.ContainsKey(key)))
            {
                var evt = new ThresholdEvent(
                    rule.Id, rule.Name, rule.EventName,
                    msg.DeviceId, msg.Capability, rule.MetricKey,
                    currentValue, rule.Value, rule.Operator,
                    DateTimeOffset.UtcNow);

                _mediator.Tell(new Publish("threshold-events", evt));
                _log.Info("Threshold event fired: {EventName} (rule={RuleName}, value={Value}, threshold={Threshold})",
                    evt.EventName, evt.RuleName, currentValue, rule.Value);
            }

            _previousValues[key] = currentValue;
        }
    }

    private static bool TryExtractNumericValue(object value, string? metricKey, out double result)
    {
        result = 0;

        if (metricKey != null)
        {
            if (value is IDictionary<string, object> dict && dict.TryGetValue(metricKey, out var inner))
                return TryConvertToDouble(inner, out result);
            return false;
        }

        return TryConvertToDouble(value, out result);
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

    private static bool ShouldFire(ThresholdOperator op, double current, double threshold, double previous, bool hasPrevious)
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

    private sealed record RulesLoaded(List<ThresholdRule> Rules);
}
