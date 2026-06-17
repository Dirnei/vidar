using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;
using Vidar.Core.Webhooks;

namespace Vidar.Communication.UniFi.Tests;

public sealed class UniFiBridgeActorWebhookTests : TestKit
{
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://UniFiBridgeActorWebhookTests@127.0.0.1:2553""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    public UniFiBridgeActorWebhookTests() : base(TestConfig, "UniFiBridgeActorWebhookTests")
    {
    }

    private void RegisterProbes(TestProbe pluginRegistry, TestProbe shardProxy, TestProbe webhookRegistry)
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(pluginRegistry);
        registry.Register<DeviceTwinRegion>(shardProxy);
        registry.Register<WebhookRegistry>(webhookRegistry);
    }

    [Fact]
    public void Bridge_RegistersBothWebhookRoutes_OnStart()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy, webhookRegistry);

        Sys.ActorOf(UniFiBridgeActor.Props("http://localhost:1"), "unifi-bridge");

        var registrations = new[]
        {
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10)),
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10))
        };

        Assert.Contains(registrations, r => r.RouteKey == "unifi-protect");
        Assert.Contains(registrations, r => r.RouteKey == "unifi-network");
        Assert.All(registrations, r => Assert.Equal(WebhookAuthMode.None, r.AuthMode));
        Assert.All(registrations, r => Assert.Equal("unifi", r.IntegrationId));
    }

    [Fact]
    public void Bridge_ReRegisters_OnWebhookRegistryStarted()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy, webhookRegistry);

        var bridge = Sys.ActorOf(
            UniFiBridgeActor.Props("http://localhost:1"), "unifi-bridge-2");

        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));

        bridge.Tell(WebhookRegistryStarted.Instance, TestActor);

        var reRegistrations = new[]
        {
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(5)),
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(5))
        };

        Assert.Contains(reRegistrations, r => r.RouteKey == "unifi-protect");
        Assert.Contains(reRegistrations, r => r.RouteKey == "unifi-network");
    }

    [Fact]
    public void Bridge_AcknowledgesFailedWebhook_OnFetchFailure()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy, webhookRegistry);

        var bridge = Sys.ActorOf(
            UniFiBridgeActor.Props("http://localhost:1"), "bridge-ack");

        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));

        var payloadId = Guid.NewGuid();
        bridge.Tell(new WebhookReceived("unifi-protect", payloadId,
            new Dictionary<string, string>(), "application/json", 0, DateTimeOffset.UtcNow), webhookRegistry.Ref);

        var handled = webhookRegistry.ExpectMsg<WebhookHandled>(TimeSpan.FromSeconds(30));
        Assert.Equal(payloadId, handled.PayloadId);
        Assert.Equal(WebhookHandleStatus.Failed, handled.Status);
        Assert.NotNull(handled.Error);
    }
}
