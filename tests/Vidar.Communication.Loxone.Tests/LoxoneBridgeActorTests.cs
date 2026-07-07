using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Vidar.Communication.Loxone;
using Vidar.Core.Sharding;
using PluginRegistry = Vidar.Core.Plugins.PluginRegistry;

namespace Vidar.Communication.Loxone.Tests;

// The bridge's ControlIds reply is the deviceId-routing seam the child actor depends on: once the
// bridge computes each control's stable deviceId via Discover(), it must hand that map back to
// whichever child sent ControlsDiscovered so the child can tag DeviceStateUpdates. This is
// verified here directly (no MQTT involved — LoxoneBridgeActor itself never touches the broker).
public sealed class LoxoneBridgeActorTests : TestKit
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

    public LoxoneBridgeActorTests() : base(TestConfig, "LoxoneBridgeActorTests") { }

    [Fact]
    public void ControlsDiscovered_RepliesWithControlIds_ForEachSupportedControl()
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(CreateTestProbe());
        registry.Register<DeviceTwinRegion>(CreateTestProbe());

        var bridge = Sys.ActorOf(LoxoneBridgeActor.Props("localhost", 1883, "loxone2mqtt"));

        var structure = new LoxoneStructure(
            "AAA",
            [
                new LoxoneControl("uuid-1", "Kitchen Switch", "Switch", "room-1", []),
                new LoxoneControl("uuid-2", "Unsupported", "UnknownType", "room-1", []),
            ],
            [new LoxoneRoom("room-1", "Kitchen")]);

        bridge.Tell(new ControlsDiscovered("AAA", structure));

        var reply = ExpectMsg<ControlIds>();
        Assert.Equal("AAA", reply.Serial);
        Assert.Single(reply.ByUuid);
        Assert.True(reply.ByUuid.ContainsKey("uuid-1"));
        Assert.NotEqual(Guid.Empty, reply.ByUuid["uuid-1"]);
        Assert.False(reply.ByUuid.ContainsKey("uuid-2")); // unsupported type: no capabilities, skipped
    }
}
