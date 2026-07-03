using Akka.Actor;
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

    // A fully-configured settings dict pointing at an unreachable broker.
    private static Dictionary<string, string> ConfiguredSettings() => new()
    {
        ["mqttHost"] = "localhost",
        ["mqttPort"] = "1883",
        ["topic"] = "ecowitt",
        ["staleAfterSeconds"] = "300",
    };

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

        Sys.ActorOf(EcowittBridgeActor.Props());

        var msg = pluginRegistry.ExpectMsg<RegisterPlugin>(TimeSpan.FromSeconds(3));
        Assert.Equal("ecowitt", msg.PluginId);
    }

    [Fact]
    public void DisabledConfig_DoesNotCrash()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        var statusProbe = CreateTestProbe();
        mediator.Tell(new Subscribe("application-status", statusProbe.Ref), statusProbe.Ref);
        statusProbe.ExpectMsg<SubscribeAck>();

        var bridge = Sys.ActorOf(EcowittBridgeActor.Props());
        Watch(bridge);

        bridge.Tell(new PluginRegistered(
            "ecowitt", false, new Dictionary<string, string>(), []), TestActor);

        statusProbe.ExpectMsg<ApplicationStatusUpdate>(
            m => m.Status == "stopped", TimeSpan.FromSeconds(3));

        // The actor must survive the disabled-config path — no Terminated on the watcher.
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void EnabledConfig_WithUnreachableBroker_DoesNotCrash()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        var statusProbe = CreateTestProbe();
        mediator.Tell(new Subscribe("application-status", statusProbe.Ref), statusProbe.Ref);
        statusProbe.ExpectMsg<SubscribeAck>();

        var bridge = Sys.ActorOf(EcowittBridgeActor.Props());
        Watch(bridge);

        // localhost:1883 is not listening in the test env; with a full config the bridge
        // attempts the connect, fails gracefully, and publishes "error" instead of crashing.
        bridge.Tell(new PluginRegistered(
            "ecowitt", true, ConfiguredSettings(), []), TestActor);

        statusProbe.ExpectMsg<ApplicationStatusUpdate>(
            m => m.Status == "error", TimeSpan.FromSeconds(10));

        // The actor must survive the failed-connect path — no Terminated on the watcher.
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void AdoptionAfterFirstPayload_RedirectsStateToAdoptedDevice()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var bridge = Sys.ActorOf(EcowittBridgeActor.Props());

        const string passkey = "ABC123DEF456";
        var payload = $"PASSKEY={passkey}&stationtype=GW3000A&tempf=68.0&humidity=63";

        // First payload arrives before the station is adopted: the actor mints a synthetic
        // device id and reports state there.
        bridge.Tell(new EcowittBridgeActor.PayloadReceived(payload), TestActor);
        var first = shardProxy.ExpectMsg<DeviceStateUpdate>(TimeSpan.FromSeconds(3));
        var syntheticId = first.DeviceId;
        shardProxy.ReceiveWhile(TimeSpan.FromMilliseconds(200), _ => 0); // drain remaining updates

        // The user adopts the station: the host registers PASSKEY -> the real device id.
        var adoptedId = Guid.NewGuid();
        bridge.Tell(new RegisterDeviceForPolling(adoptedId, "ecowitt", passkey, "", 0, []), TestActor);

        // The next payload must now route state to the adopted device, not the synthetic id.
        bridge.Tell(new EcowittBridgeActor.PayloadReceived(payload), TestActor);
        var afterAdopt = shardProxy.ExpectMsg<DeviceStateUpdate>(TimeSpan.FromSeconds(3));

        Assert.NotEqual(syntheticId, adoptedId); // sanity: the ids really differ
        Assert.Equal(adoptedId, afterAdopt.DeviceId);
    }

    [Fact]
    public void EnabledButNotConfigured_StaysIdle_NoBrokerContact()
    {
        var pluginRegistry = CreateTestProbe();
        var shardProxy = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var mediator = DistributedPubSub.Get(Sys).Mediator;
        var statusProbe = CreateTestProbe();
        mediator.Tell(new Subscribe("application-status", statusProbe.Ref), statusProbe.Ref);
        statusProbe.ExpectMsg<SubscribeAck>();

        var bridge = Sys.ActorOf(EcowittBridgeActor.Props());
        Watch(bridge);

        // Enabled but no MQTT host/topic supplied — the actor must not dial any broker.
        // It reports "unconfigured" and never attempts a connection (so never "error").
        bridge.Tell(new PluginRegistered(
            "ecowitt", true, new Dictionary<string, string>(), []), TestActor);

        statusProbe.ExpectMsg<ApplicationStatusUpdate>(
            m => m.Status == "unconfigured", TimeSpan.FromSeconds(3));

        // No "error" (or any further) status should follow, and the actor stays alive
        // even across a health-tick — confirming it never touched a broker.
        statusProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));
    }
}
