using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Actors;

public sealed class ThresholdEvaluatorActorTests : TestKit
{
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://test@127.0.0.1:2552""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    private readonly IThresholdRuleRepository _ruleRepo = Substitute.For<IThresholdRuleRepository>();
    private readonly IThresholdEventLogRepository _eventLogRepo = Substitute.For<IThresholdEventLogRepository>();
    private readonly Guid _deviceId = Guid.NewGuid();

    public ThresholdEvaluatorActorTests() : base(TestConfig, "ThresholdEvaluatorTests") { }

    private IActorRef CreateActor(List<ThresholdRule>? rules = null)
    {
        _ruleRepo.GetAllAsync().Returns(rules ?? []);
        return Sys.ActorOf(ThresholdEvaluatorActor.Props(_ruleRepo, _eventLogRepo));
    }

    [Fact]
    public void GreaterThan_FiresWhenValueExceedsThreshold()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "High Power", DeviceId = _deviceId,
            CapabilityKey = "power", Operator = ThresholdOperator.GreaterThan,
            Value = 100, EventName = "high_power"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();

        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, "power", 150.0, DateTime.UtcNow));

        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("high_power", evt.EventName);
        Assert.Equal(150.0, evt.CurrentValue);
        Assert.Equal(100.0, evt.ThresholdValue);
    }

    [Fact]
    public void GreaterThan_DoesNotFireWhenBelowThreshold()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "High Power", DeviceId = _deviceId,
            CapabilityKey = "power", Operator = ThresholdOperator.GreaterThan,
            Value = 100, EventName = "high_power"
        };
        var actor = CreateActor([rule]);
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, "power", 50.0, DateTime.UtcNow));

        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void CrossesAbove_FiresOnlyOnTransition()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "SoC Full", DeviceId = _deviceId,
            CapabilityKey = "battery", Operator = ThresholdOperator.CrossesAbove,
            Value = 80, EventName = "soc_full"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // First update below threshold — no event
        actor.Tell(new DeviceStateChanged(_deviceId, "battery", 70.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Second update crosses above — fires
        actor.Tell(new DeviceStateChanged(_deviceId, "battery", 85.0, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("soc_full", evt.EventName);

        // Third update still above — should NOT fire again
        actor.Tell(new DeviceStateChanged(_deviceId, "battery", 90.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void CrossesBelow_FiresOnlyOnTransition()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "SoC Low", DeviceId = _deviceId,
            CapabilityKey = "battery", Operator = ThresholdOperator.CrossesBelow,
            Value = 20, EventName = "soc_low"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // Start above
        actor.Tell(new DeviceStateChanged(_deviceId, "battery", 30.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Cross below
        actor.Tell(new DeviceStateChanged(_deviceId, "battery", 15.0, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("soc_low", evt.EventName);

        // Stay below — should NOT fire again
        actor.Tell(new DeviceStateChanged(_deviceId, "battery", 10.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void GreaterThanOrEqual_FiresOnExactThreshold()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Autarky High", DeviceId = _deviceId,
            CapabilityKey = "autarky",
            Operator = ThresholdOperator.GreaterThanOrEqual, Value = 90,
            EventName = "autarky_high"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, "autarky", 95.0, DateTime.UtcNow));

        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("autarky_high", evt.EventName);
        Assert.Equal(95.0, evt.CurrentValue);
    }

    [Fact]
    public void DisabledRule_DoesNotFire()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Disabled", DeviceId = _deviceId,
            CapabilityKey = "power", Operator = ThresholdOperator.GreaterThan,
            Value = 50, EventName = "should_not_fire", Enabled = false
        };
        var actor = CreateActor([rule]);
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, "power", 100.0, DateTime.UtcNow));

        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void AddThresholdRule_AddsRuleAtRuntime()
    {
        var actor = CreateActor([]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "New Rule", DeviceId = _deviceId,
            CapabilityKey = "power", Operator = ThresholdOperator.GreaterThan,
            Value = 100, EventName = "new_rule"
        };
        actor.Tell(new AddThresholdRule(rule));
        Thread.Sleep(200);

        actor.Tell(new DeviceStateChanged(_deviceId, "power", 200.0, DateTime.UtcNow));

        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("new_rule", evt.EventName);
    }

    [Fact]
    public void BecomesTrue_FiresOnTransitionToTrue()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Motion Detected", DeviceId = _deviceId,
            CapabilityKey = "motion", Operator = ThresholdOperator.BecomesTrue,
            EventName = "motion_detected"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // Start with false
        actor.Tell(new DeviceStateChanged(_deviceId, "motion", false, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Transition to true — fires
        actor.Tell(new DeviceStateChanged(_deviceId, "motion", true, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("motion_detected", evt.EventName);

        // Stay true — should NOT fire again
        actor.Tell(new DeviceStateChanged(_deviceId, "motion", true, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void BecomesFalse_FiresOnTransitionToFalse()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Contact Closed", DeviceId = _deviceId,
            CapabilityKey = "contact", Operator = ThresholdOperator.BecomesFalse,
            EventName = "contact_closed"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // Start with true
        actor.Tell(new DeviceStateChanged(_deviceId, "contact", true, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Transition to false — fires
        actor.Tell(new DeviceStateChanged(_deviceId, "contact", false, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("contact_closed", evt.EventName);
    }

    [Fact]
    public void Changes_FiresOnAnyValueChange()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "State Changed", DeviceId = _deviceId,
            CapabilityKey = "switch", Operator = ThresholdOperator.Changes,
            EventName = "switch_changed"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // First value — no previous, so no change event
        actor.Tell(new DeviceStateChanged(_deviceId, "switch", true, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Different value — fires
        actor.Tell(new DeviceStateChanged(_deviceId, "switch", false, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("switch_changed", evt.EventName);
    }

    [Fact]
    public void Equals_FiresWhenStringMatches()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Action Match", DeviceId = _deviceId,
            CapabilityKey = "action", Operator = ThresholdOperator.Equals,
            StringValue = "single", EventName = "button_single"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, "action", "single", DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("button_single", evt.EventName);

        // Different value — should NOT fire
        actor.Tell(new DeviceStateChanged(_deviceId, "action", "double", DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void NotEquals_FiresWhenStringDoesNotMatch()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Not Idle", DeviceId = _deviceId,
            CapabilityKey = "status", Operator = ThresholdOperator.NotEquals,
            StringValue = "idle", EventName = "status_not_idle"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, "status", "running", DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("status_not_idle", evt.EventName);

        // Matching value — should NOT fire
        actor.Tell(new DeviceStateChanged(_deviceId, "status", "idle", DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
