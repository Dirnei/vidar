using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Actors;

public sealed record SetWebhookDependencies(IActorRef SseActor, IWebhookEventRepository EventRepo);

/// <summary>
/// Cluster singleton holding the webhook route table. Listeners register point-to-point
/// (no pub/sub — exactly one recipient per route). Registration is idempotent and
/// last-write-wins; listeners are DeathWatched and their routes removed on termination.
/// Every state change is mirrored into IWebhookRouteCache for the HTTP hot path.
/// NOTE: the cache lives in this process — this only works while the HTTP host and this
/// singleton share the single "host"-role node. Scaling the host to multiple nodes
/// requires cross-node route propagation first.
/// </summary>
public sealed class WebhookRegistryActor : ReceiveActor
{
    private static readonly Regex RouteKeyPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IWebhookRouteCache _routeCache;
    private readonly Dictionary<string, RegisterWebhookListener> _routes = new();
    private IActorRef? _sseActor;
    private IWebhookEventRepository? _eventRepo;

    public static Props Props(IWebhookRouteCache routeCache) =>
        Akka.Actor.Props.Create(() => new WebhookRegistryActor(routeCache));

    public WebhookRegistryActor(IWebhookRouteCache routeCache)
    {
        _routeCache = routeCache;

        Receive<SetWebhookDependencies>(msg =>
        {
            _sseActor = msg.SseActor;
            _eventRepo = msg.EventRepo;
        });

        Receive<RegisterWebhookListener>(msg =>
        {
            if (!RouteKeyPattern.IsMatch(msg.RouteKey))
            {
                _log.Warning("Rejected webhook registration from {Listener}: route key '{RouteKey}' " +
                             "is invalid (must match [a-z0-9-]+)", msg.Listener, msg.RouteKey);
                return;
            }

            var isTakeover = _routes.TryGetValue(msg.RouteKey, out var existing) &&
                             !existing.Listener.Equals(msg.Listener);
            if (isTakeover)
                _log.Warning("Webhook route '{RouteKey}' taken over by {New} (was {Old})",
                    msg.RouteKey, msg.Listener, existing!.Listener);

            _routes[msg.RouteKey] = msg;
            if (isTakeover)
                UnwatchIfUnused(existing!.Listener);

            // Watch is idempotent in Akka; re-registrations don't stack watches.
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
            _sseActor?.Tell(msg);
        });

        Receive<OAuthCallbackReceived>(msg =>
        {
            var routeKey = $"oauth-{msg.IntegrationId}";
            if (_routes.TryGetValue(routeKey, out var route))
                route.Listener.Tell(msg);
            else
                _log.Warning("OAuth callback for '{IntegrationId}' dropped — no listener on route '{RouteKey}'",
                    msg.IntegrationId, routeKey);
        });

        ReceiveAsync<WebhookHandled>(async msg =>
        {
            _sseActor?.Tell(msg);
            if (_eventRepo != null)
            {
                try
                {
                    await _eventRepo.AcknowledgeAsync(
                        msg.PayloadId, msg.Status.ToString().ToLowerInvariant(), msg.Error, msg.HandledAt);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Failed to persist webhook acknowledgement for {PayloadId}", msg.PayloadId);
                }
            }
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

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("webhook-registry-started", WebhookRegistryStarted.Instance));
        _log.Info("Webhook registry started, notified subscribers");
    }

    private void UnwatchIfUnused(IActorRef listener)
    {
        if (!_routes.Values.Any(r => r.Listener.Equals(listener)))
            Context.Unwatch(listener);
    }

    private void PushRouteCache() =>
        _routeCache.UpdateRoutes(_routes.ToDictionary(
            kv => kv.Key,
            kv => new WebhookRouteInfo(kv.Value.AuthMode, kv.Value.Secret, kv.Value.HeaderName, kv.Value.IntegrationId)));
}
