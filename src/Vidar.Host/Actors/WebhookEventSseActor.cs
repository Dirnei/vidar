using System.Threading.Channels;
using Akka.Actor;
using Vidar.Core.Messages;

namespace Vidar.Host.Actors;

public sealed record WebhookReceivedNotification(
    Guid PayloadId, string RouteKey, string? IntegrationId,
    string ContentType, long ContentLength, DateTimeOffset ReceivedAt);

public sealed record WebhookHandledNotification(
    Guid PayloadId, string Status, string? Error, DateTimeOffset HandledAt);

public sealed record RegisterWebhookSseClient(Channel<object> Channel);
public sealed record UnregisterWebhookSseClient(Channel<object> Channel);

public sealed class WebhookEventSseActor : ReceiveActor
{
    private readonly HashSet<Channel<object>> _clients = new();

    public static Props Props() => Akka.Actor.Props.Create(() => new WebhookEventSseActor());

    public WebhookEventSseActor()
    {
        Receive<WebhookReceived>(msg =>
        {
            var notification = new WebhookReceivedNotification(
                msg.PayloadId, msg.RouteKey, null, msg.ContentType, msg.ContentLength, msg.ReceivedAt);
            foreach (var client in _clients)
                client.Writer.TryWrite(notification);
        });
        Receive<WebhookHandled>(msg =>
        {
            var notification = new WebhookHandledNotification(
                msg.PayloadId, msg.Status.ToString().ToLowerInvariant(), msg.Error, msg.HandledAt);
            foreach (var client in _clients)
                client.Writer.TryWrite(notification);
        });
        Receive<RegisterWebhookSseClient>(msg => _clients.Add(msg.Channel));
        Receive<UnregisterWebhookSseClient>(msg =>
        {
            _clients.Remove(msg.Channel);
            msg.Channel.Writer.TryComplete();
        });
    }
}
