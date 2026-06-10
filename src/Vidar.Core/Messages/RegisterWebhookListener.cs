using Akka.Actor;

namespace Vidar.Core.Messages;

public sealed record RegisterWebhookListener(
    string RouteKey,
    IActorRef Listener,
    WebhookAuthMode AuthMode = WebhookAuthMode.None,
    string? Secret = null,
    string? HeaderName = null);
