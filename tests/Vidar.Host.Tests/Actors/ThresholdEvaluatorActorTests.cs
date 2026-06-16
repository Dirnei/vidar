using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Capabilities;
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
    private readonly Guid _deviceId = Guid.NewGuid();

    public ThresholdEvaluatorActorTests() : base(TestConfig, "ThresholdEvaluatorTests") { }

    private IActorRef CreateActor(List<ThresholdRule>? rules = null)
    {
        _ruleRepo.GetAllAsync().Returns(rules ?? []);
        return Sys.ActorOf(ThresholdEvaluatorActor.Props(_ruleRepo));
    }

    [Fact]
    public void GreaterThan_FiresWhenValueExceedsThreshold()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "High Power", DeviceId = _deviceId,
            Capability = CapabilityType.Power, Operator = ThresholdOperator.GreaterThan,
            Value = 100, EventName = "high_power"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();

        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Power, 150.0, DateTime.UtcNow));

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
            Capability = CapabilityType.Power, Operator = ThresholdOperator.GreaterThan,
            Value = 100, EventName = "high_power"
        };
        var actor = CreateActor([rule]);
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Power, 50.0, DateTime.UtcNow));

        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void CrossesAbove_FiresOnlyOnTransition()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "SoC Full", DeviceId = _deviceId,
            Capability = CapabilityType.Battery, Operator = ThresholdOperator.CrossesAbove,
            Value = 80, EventName = "soc_full"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // First update below threshold — no event
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Battery, 70.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Second update crosses above — fires
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Battery, 85.0, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("soc_full", evt.EventName);

        // Third update still above — should NOT fire again
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Battery, 90.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void CrossesBelow_FiresOnlyOnTransition()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "SoC Low", DeviceId = _deviceId,
            Capability = CapabilityType.Battery, Operator = ThresholdOperator.CrossesBelow,
            Value = 20, EventName = "soc_low"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        // Start above
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Battery, 30.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Cross below
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Battery, 15.0, DateTime.UtcNow));
        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("soc_low", evt.EventName);

        // Stay below — should NOT fire again
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Battery, 10.0, DateTime.UtcNow));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void MetricKey_ExtractsValueFromExtrasDict()
    {
        var rule = new ThresholdRule
        {
            Id = Guid.NewGuid(), Name = "Autarky High", DeviceId = _deviceId,
            Capability = CapabilityType.Extras, MetricKey = "autarky",
            Operator = ThresholdOperator.GreaterThanOrEqual, Value = 90,
            EventName = "autarky_high"
        };
        var actor = CreateActor([rule]);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        mediator.Tell(new Subscribe("threshold-events", TestActor));
        ExpectMsg<SubscribeAck>();
        Thread.Sleep(500);

        var extras = new Dictionary<string, object> { ["autarky"] = 95.0, ["selfConsumption"] = 80.0 };
        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Extras, extras, DateTime.UtcNow));

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
            Capability = CapabilityType.Power, Operator = ThresholdOperator.GreaterThan,
            Value = 50, EventName = "should_not_fire", Enabled = false
        };
        var actor = CreateActor([rule]);
        Thread.Sleep(500);

        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Power, 100.0, DateTime.UtcNow));

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
            Capability = CapabilityType.Power, Operator = ThresholdOperator.GreaterThan,
            Value = 100, EventName = "new_rule"
        };
        actor.Tell(new AddThresholdRule(rule));
        Thread.Sleep(200);

        actor.Tell(new DeviceStateChanged(_deviceId, CapabilityType.Power, 200.0, DateTime.UtcNow));

        var evt = ExpectMsg<ThresholdEvent>(TimeSpan.FromSeconds(3));
        Assert.Equal("new_rule", evt.EventName);
    }
}
