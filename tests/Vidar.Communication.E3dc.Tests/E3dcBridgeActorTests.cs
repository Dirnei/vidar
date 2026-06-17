using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

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

    private void RegisterProbes(TestProbe pluginRegistry, TestProbe shardProxy)
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(pluginRegistry);
        registry.Register<DeviceTwinRegion>(shardProxy);
    }

    [Fact]
    public void ReceivesPluginRegistered_WithoutCrash()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var bridge = Sys.ActorOf(E3dcBridgeActor.Props());

        bridge.Tell(new PluginRegistered(
            "e3dc", false, new Dictionary<string, string>(), []), TestActor);

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void RegistersWithPluginRegistryOnStart()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        Sys.ActorOf(E3dcBridgeActor.Props());

        var msg = pluginRegistry.ExpectMsg<RegisterPlugin>(TimeSpan.FromSeconds(3));
        Assert.Equal("e3dc", msg.PluginId);
    }

    [Fact]
    public void DisabledConfig_DoesNotStartClient()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var bridge = Sys.ActorOf(E3dcBridgeActor.Props());

        bridge.Tell(new PluginRegistered(
            "e3dc", false, new Dictionary<string, string>(), []), TestActor);

        bridge.Tell(new PluginRegistered(
            "e3dc", false, new Dictionary<string, string>(), []), TestActor);
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
