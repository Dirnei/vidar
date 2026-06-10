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

        Sys.ActorOf(UniFiBridgeActor.Props(shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1"), "unifi-bridge");

        var registrations = new[]
        {
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10)),
            webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10))
        };

        Assert.Contains(registrations, r => r.RouteKey == "unifi-protect");
        Assert.Contains(registrations, r => r.RouteKey == "unifi-network");
        Assert.All(registrations, r => Assert.Equal(WebhookAuthMode.None, r.AuthMode));
    }

    [Fact]
    public void Bridge_ReRegisters_Periodically()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();

        // Re-registration interval is parameterized so this test doesn't wait 60s
        Sys.ActorOf(UniFiBridgeActor.Props(
            shardProxy.Ref, webhookRegistry.Ref, "http://localhost:1", TimeSpan.FromMilliseconds(200)), "unifi-bridge-2");

        // initial round + at least one re-registration round
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
        webhookRegistry.ExpectMsg<RegisterWebhookListener>(TimeSpan.FromSeconds(10));
    }
}
