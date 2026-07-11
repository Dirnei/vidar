using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Actors;

public sealed class WebhookRegistryActorTests : TestKit
{
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://WebhookRegistryActorTests@127.0.0.1:2553""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    private readonly IWebhookRouteCache _cache = Substitute.For<IWebhookRouteCache>();
    private readonly IWebhookEventRepository _eventRepo = Substitute.For<IWebhookEventRepository>();

    public WebhookRegistryActorTests() : base(TestConfig, "WebhookRegistryActorTests")
    {
    }

    private IActorRef CreateRegistry(IActorRef? sseActor = null)
    {
        var registry = Sys.ActorOf(WebhookRegistryActor.Props(_cache));
        registry.Tell(new SetWebhookDependencies(sseActor ?? TestActor, _eventRepo));
        return registry;
    }

    [Fact]
    public void Register_PushesRouteToCache()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref, WebhookAuthMode.UrlSecret, "s3cret"));

        AwaitAssert(() => _cache.Received().UpdateRoutes(Arg.Is<Dictionary<string, WebhookRouteInfo>>(d =>
            d.ContainsKey("unifi-protect") &&
            d["unifi-protect"].AuthMode == WebhookAuthMode.UrlSecret &&
            d["unifi-protect"].Secret == "s3cret")));
    }

    [Fact]
    public void WebhookReceived_ForwardsToRegisteredListener()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));

        var evt = new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 42, DateTimeOffset.UtcNow);
        registry.Tell(evt);

        listener.ExpectMsg<WebhookReceived>(m => m.PayloadId == evt.PayloadId);
    }

    [Fact]
    public void WebhookReceived_NoListener_IsDroppedWithoutCrash()
    {
        var registry = CreateRegistry();

        registry.Tell(new WebhookReceived("unknown", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));

        // Actor must still be alive and functional afterwards
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("x", listener.Ref));
        registry.Tell(new WebhookReceived("x", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        listener.ExpectMsg<WebhookReceived>();
    }

    [Fact]
    public void Register_SameKeyTwice_LastWriteWins()
    {
        var registry = CreateRegistry();
        var first = CreateTestProbe();
        var second = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", first.Ref));
        registry.Tell(new RegisterWebhookListener("unifi-protect", second.Ref));

        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));

        second.ExpectMsg<WebhookReceived>();
        first.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Register_IsIdempotent_SameListenerRefreshes()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));

        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        listener.ExpectMsg<WebhookReceived>();
    }

    [Fact]
    public void ListenerTerminated_RemovesItsRoutes()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));
        registry.Tell(new RegisterWebhookListener("unifi-network", listener.Ref));

        Sys.Stop(listener.Ref);

        AwaitAssert(() => _cache.Received().UpdateRoutes(Arg.Is<Dictionary<string, WebhookRouteInfo>>(d => d.Count == 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unifi-Protect")]
    [InlineData("unifi protect")]
    [InlineData("unifi/protect")]
    public void Register_InvalidRouteKey_IsRejected(string routeKey)
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener(routeKey, listener.Ref));

        registry.Tell(new WebhookReceived(routeKey, Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        listener.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Unregister_RemovesRoute_OnlyForOwningListener()
    {
        var registry = CreateRegistry();
        var owner = CreateTestProbe();
        var imposter = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", owner.Ref));

        // Imposter cannot unregister someone else's route
        registry.Tell(new UnregisterWebhookListener("unifi-protect", imposter.Ref));
        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        owner.ExpectMsg<WebhookReceived>();

        // Owner can
        registry.Tell(new UnregisterWebhookListener("unifi-protect", owner.Ref));
        registry.Tell(new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 0, DateTimeOffset.UtcNow));
        owner.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Register_WithIntegrationId_FlowsIntoCacheSnapshot()
    {
        var registry = CreateRegistry();
        var listener = CreateTestProbe();

        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref, IntegrationId: "unifi"));

        AwaitAssert(() => _cache.Received().UpdateRoutes(Arg.Is<Dictionary<string, WebhookRouteInfo>>(d =>
            d.ContainsKey("unifi-protect") &&
            d["unifi-protect"].IntegrationId == "unifi")));
    }

    [Fact]
    public void WebhookReceived_TellsSseActorDirectly()
    {
        var sseProbe = CreateTestProbe();
        var registry = CreateRegistry(sseProbe.Ref);
        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("unifi-protect", listener.Ref));

        var evt = new WebhookReceived("unifi-protect", Guid.NewGuid(), new Dictionary<string, string>(),
            "application/json", 42, DateTimeOffset.UtcNow);
        registry.Tell(evt);

        sseProbe.ExpectMsg<WebhookReceived>(m => m.PayloadId == evt.PayloadId);
    }

    [Fact]
    public void WebhookHandled_TellsSseActor_AndPersists()
    {
        var sseProbe = CreateTestProbe();
        var registry = CreateRegistry(sseProbe.Ref);

        var handled = new WebhookHandled(Guid.NewGuid(), WebhookHandleStatus.Handled, null, DateTimeOffset.UtcNow);
        registry.Tell(handled);

        sseProbe.ExpectMsg<WebhookHandled>(m => m.PayloadId == handled.PayloadId);
        AwaitAssert(() => _eventRepo.Received().AcknowledgeAsync(
            handled.PayloadId, "handled", null, Arg.Any<DateTimeOffset>()));
    }

    [Fact]
    public void WebhookHandled_Failed_PersistsError()
    {
        var sseProbe = CreateTestProbe();
        var registry = CreateRegistry(sseProbe.Ref);

        var handled = new WebhookHandled(Guid.NewGuid(), WebhookHandleStatus.Failed, "parse error", DateTimeOffset.UtcNow);
        registry.Tell(handled);

        sseProbe.ExpectMsg<WebhookHandled>();
        AwaitAssert(() => _eventRepo.Received().AcknowledgeAsync(
            handled.PayloadId, "failed", "parse error", Arg.Any<DateTimeOffset>()));
    }

    [Fact]
    public void OAuthCallbackReceived_RouteExists_ForwardsToListener()
    {
        var routeCache = Substitute.For<IWebhookRouteCache>();
        var registry = Sys.ActorOf(WebhookRegistryActor.Props(routeCache));

        var listener = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("oauth-homeconnect", listener, IntegrationId: "homeconnect"));

        var callback = new OAuthCallbackReceived("homeconnect", "code-123", "state-xyz", DateTimeOffset.UtcNow, "http://localhost/api/oauth/homeconnect/callback");
        registry.Tell(callback);

        listener.ExpectMsg<OAuthCallbackReceived>(msg =>
            msg.IntegrationId == "homeconnect" &&
            msg.Code == "code-123" &&
            msg.State == "state-xyz");
    }

    [Fact]
    public void OAuthCallbackReceived_NoRoute_DoesNotCrash()
    {
        var routeCache = Substitute.For<IWebhookRouteCache>();
        var registry = Sys.ActorOf(WebhookRegistryActor.Props(routeCache));

        var callback = new OAuthCallbackReceived("homeconnect", "code-123", "state-xyz", DateTimeOffset.UtcNow, "http://localhost/api/oauth/homeconnect/callback");
        registry.Tell(callback);

        // Actor stays alive — register something after to prove it
        var probe = CreateTestProbe();
        registry.Tell(new RegisterWebhookListener("test-route", probe, IntegrationId: "test"));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }
}
