using System.Threading.Channels;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;
using Vidar.Host.Actors;

namespace Vidar.Host.Tests.Actors;

public sealed class WebhookEventSseActorTests : TestKit
{
    [Fact]
    public async Task WebhookReceived_WritesToRegisteredClients()
    {
        var actor = Sys.ActorOf(WebhookEventSseActor.Props());
        var channel = Channel.CreateBounded<object>(10);
        actor.Tell(new RegisterWebhookSseClient(channel), TestActor);

        var msg = new WebhookReceived("unifi-protect", Guid.NewGuid(),
            new Dictionary<string, string>(), "application/json", 42, DateTimeOffset.UtcNow);
        actor.Tell(msg, TestActor);

        var notification = await channel.Reader.ReadAsync();
        var received = Assert.IsType<WebhookReceivedNotification>(notification);
        Assert.Equal("unifi-protect", received.RouteKey);
        Assert.Equal(msg.PayloadId, received.PayloadId);
    }

    [Fact]
    public async Task WebhookHandled_WritesToRegisteredClients()
    {
        var actor = Sys.ActorOf(WebhookEventSseActor.Props());
        var channel = Channel.CreateBounded<object>(10);
        actor.Tell(new RegisterWebhookSseClient(channel), TestActor);

        var payloadId = Guid.NewGuid();
        actor.Tell(new WebhookHandled(payloadId, WebhookHandleStatus.Failed, "parse error", DateTimeOffset.UtcNow), TestActor);

        var notification = await channel.Reader.ReadAsync();
        var handled = Assert.IsType<WebhookHandledNotification>(notification);
        Assert.Equal(payloadId, handled.PayloadId);
        Assert.Equal("failed", handled.Status);
        Assert.Equal("parse error", handled.Error);
    }

    [Fact]
    public async Task Unregister_StopsReceivingEvents()
    {
        var actor = Sys.ActorOf(WebhookEventSseActor.Props());
        var channel = Channel.CreateBounded<object>(10);
        actor.Tell(new RegisterWebhookSseClient(channel), TestActor);
        actor.Tell(new UnregisterWebhookSseClient(channel), TestActor);

        actor.Tell(new WebhookReceived("x", Guid.NewGuid(),
            new Dictionary<string, string>(), "text/plain", 0, DateTimeOffset.UtcNow), TestActor);

        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await channel.Reader.ReadAsync());
    }
}
