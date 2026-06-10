using Akka.Actor;

namespace Vidar.Core.Messages;

public sealed record UnregisterWebhookListener(string RouteKey, IActorRef Listener);
