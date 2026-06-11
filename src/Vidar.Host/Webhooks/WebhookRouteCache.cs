namespace Vidar.Host.Webhooks;

/// <summary>
/// Route table snapshot written by the WebhookRegistryActor, read lock-free on the HTTP hot path.
/// Updates swap the whole dictionary reference; readers never see a partially updated table.
/// </summary>
public sealed class WebhookRouteCache : IWebhookRouteCache
{
    private volatile Dictionary<string, WebhookRouteInfo> _routes = new();

    public bool TryGetRoute(string routeKey, out WebhookRouteInfo route) =>
        _routes.TryGetValue(routeKey, out route!);

    public IReadOnlyDictionary<string, WebhookRouteInfo> Snapshot() => _routes;

    public void UpdateRoutes(Dictionary<string, WebhookRouteInfo> routes) =>
        _routes = routes;
}
