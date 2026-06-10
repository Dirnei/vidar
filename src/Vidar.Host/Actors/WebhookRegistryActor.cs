using Akka.Actor;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Actors;

/// <summary>
/// Cluster singleton holding the webhook route table. Listeners register point-to-point
/// (no pub/sub — exactly one recipient per route). Registration is idempotent and
/// last-write-wins; listeners are DeathWatched and their routes removed on termination.
/// Every state change is mirrored into IWebhookRouteCache for the HTTP hot path.
/// </summary>
public sealed class WebhookRegistryActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IWebhookRouteCache _routeCache;
    private readonly Dictionary<string, RegisterWebhookListener> _routes = new();

    public static Props Props(IWebhookRouteCache routeCache) =>
        Akka.Actor.Props.Create(() => new WebhookRegistryActor(routeCache));

    public WebhookRegistryActor(IWebhookRouteCache routeCache)
    {
        _routeCache = routeCache;

        Receive<RegisterWebhookListener>(msg =>
        {
            if (_routes.TryGetValue(msg.RouteKey, out var existing) && !existing.Listener.Equals(msg.Listener))
            {
                _log.Warning("Webhook route '{RouteKey}' taken over by {New} (was {Old})",
                    msg.RouteKey, msg.Listener, existing.Listener);
                _routes[msg.RouteKey] = msg;
                UnwatchIfUnused(existing.Listener);
            }
            else
            {
                _routes[msg.RouteKey] = msg;
            }

            Context.Watch(msg.Listener);
            PushRouteCache();
        });

        Receive<UnregisterWebhookListener>(msg =>
        {
            if (!_routes.TryGetValue(msg.RouteKey, out var existing) || !existing.Listener.Equals(msg.Listener))
                return;
            _routes.Remove(msg.RouteKey);
            UnwatchIfUnused(msg.Listener);
            PushRouteCache();
            _log.Info("Webhook route '{RouteKey}' unregistered", msg.RouteKey);
        });

        Receive<WebhookReceived>(msg =>
        {
            if (_routes.TryGetValue(msg.RouteKey, out var route))
                route.Listener.Tell(msg);
            else
                _log.Info("Webhook for '{RouteKey}' dropped — no listener registered", msg.RouteKey);
        });

        Receive<Terminated>(t =>
        {
            var removed = _routes.Where(kv => kv.Value.Listener.Equals(t.ActorRef))
                .Select(kv => kv.Key).ToList();
            if (removed.Count == 0) return;
            foreach (var key in removed)
                _routes.Remove(key);
            _log.Info("Webhook listener {Listener} terminated, removed routes: {Routes}",
                t.ActorRef, string.Join(", ", removed));
            PushRouteCache();
        });
    }

    private void UnwatchIfUnused(IActorRef listener)
    {
        if (!_routes.Values.Any(r => r.Listener.Equals(listener)))
            Context.Unwatch(listener);
    }

    private void PushRouteCache() =>
        _routeCache.UpdateRoutes(_routes.ToDictionary(
            kv => kv.Key,
            kv => new WebhookRouteInfo(kv.Value.AuthMode, kv.Value.Secret, kv.Value.HeaderName)));
}
