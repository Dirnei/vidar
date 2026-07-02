using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Vidar.Communication.Ecowitt;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Ecowitt.Tests;

public sealed class EcowittBridgeActorTests : TestKit
{
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://EcowittBridgeActorTests@127.0.0.1:2559""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    private static readonly EcowittConfig Config =
        new("localhost", 1883, null, null, "ecowitt", 300);

    public EcowittBridgeActorTests() : base(TestConfig, "EcowittBridgeActorTests")
    {
    }

    private void RegisterProbes(TestProbe pluginRegistry, TestProbe shardProxy)
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(pluginRegistry);
        registry.Register<DeviceTwinRegion>(shardProxy);
    }

    [Fact]
    public void RegistersWithPluginRegistryOnStart()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        Sys.ActorOf(EcowittBridgeActor.Props(Config));

        var msg = pluginRegistry.ExpectMsg<RegisterPlugin>(TimeSpan.FromSeconds(3));
        Assert.Equal("ecowitt", msg.PluginId);
    }

    [Fact]
    public void DisabledConfig_DoesNotCrash()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var bridge = Sys.ActorOf(EcowittBridgeActor.Props(Config));

        bridge.Tell(new PluginRegistered(
            "ecowitt", false, new Dictionary<string, string>(), []), TestActor);

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void EnabledConfig_WithUnreachableBroker_DoesNotCrash()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var bridge = Sys.ActorOf(EcowittBridgeActor.Props(Config));

        // localhost:1883 is not listening in the test env; connect fails gracefully.
        bridge.Tell(new PluginRegistered(
            "ecowitt", true, new Dictionary<string, string>(), []), TestActor);

        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
