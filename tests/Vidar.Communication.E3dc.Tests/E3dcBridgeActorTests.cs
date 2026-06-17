using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;

namespace Vidar.Communication.E3dc.Tests;

public sealed class E3dcBridgeActorTests : TestKit
{
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://E3dcBridgeActorTests@127.0.0.1:2553""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    public E3dcBridgeActorTests() : base(TestConfig, "E3dcBridgeActorTests")
    {
    }

    [Fact]
    public void ReceivesPluginRegistered_WithoutCrash()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        var bridge = Sys.ActorOf(E3dcBridgeActor.Props(pluginRegistry, shardProxy));

        bridge.Tell(new PluginRegistered(
            "e3dc", false, new Dictionary<string, string>(), []), TestActor);

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void RegistersWithPluginRegistryOnStart()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        Sys.ActorOf(E3dcBridgeActor.Props(pluginRegistry, shardProxy));

        var msg = pluginRegistry.ExpectMsg<RegisterPlugin>(TimeSpan.FromSeconds(3));
        Assert.Equal("e3dc", msg.PluginId);
    }

    [Fact]
    public void DisabledConfig_DoesNotStartClient()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        var bridge = Sys.ActorOf(E3dcBridgeActor.Props(pluginRegistry, shardProxy));

        bridge.Tell(new PluginRegistered(
            "e3dc", false, new Dictionary<string, string>(), []), TestActor);

        // Should stay alive, no crash
        bridge.Tell(new PluginRegistered(
            "e3dc", false, new Dictionary<string, string>(), []), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
