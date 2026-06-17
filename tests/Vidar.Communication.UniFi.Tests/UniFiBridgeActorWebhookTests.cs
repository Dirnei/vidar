using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;

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

    [Fact]
    public void Bridge_RegistersBothWebhookRoutes_OnStart()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();

        Sys.ActorOf(UniFiBridgeActor.Props(shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1", pluginRegistry.Ref), "unifi-bridge");

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

        var bridge = Sys.ActorOf(
            UniFiBridgeActor.Props(shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1", pluginRegistry.Ref), "unifi-bridge-2");

        // drain initial registration
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));

        // simulate registry singleton restart
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

        var bridge = Sys.ActorOf(
            UniFiBridgeActor.Props(shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1", pluginRegistry.Ref), "bridge-ack");

        // drain initial registrations
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));

        // Send a webhook — the bridge will try to fetch payload from http://localhost:1 which will fail
        var payloadId = Guid.NewGuid();
        bridge.Tell(new WebhookReceived("unifi-protect", payloadId,
            new Dictionary<string, string>(), "application/json", 0, DateTimeOffset.UtcNow), webhookRegistry.Ref);

        // Bridge should acknowledge with Failed status since the HTTP fetch will fail
        var handled = webhookRegistry.ExpectMsg<WebhookHandled>(TimeSpan.FromSeconds(30));
        Assert.Equal(payloadId, handled.PayloadId);
        Assert.Equal(WebhookHandleStatus.Failed, handled.Status);
        Assert.NotNull(handled.Error);
    }
}
