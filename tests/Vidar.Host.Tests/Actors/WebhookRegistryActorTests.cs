using Akka.Actor;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Host.Actors;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Actors;

public sealed class WebhookRegistryActorTests : TestKit
{
    private readonly IWebhookRouteCache _cache = Substitute.For<IWebhookRouteCache>();

    private IActorRef CreateRegistry() => Sys.ActorOf(WebhookRegistryActor.Props(_cache));

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
}
