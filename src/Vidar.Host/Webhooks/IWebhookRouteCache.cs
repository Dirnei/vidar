using Vidar.Core.Messages;

namespace Vidar.Host.Webhooks;

public sealed record WebhookRouteInfo(WebhookAuthMode AuthMode, string? Secret, string? HeaderName);

public interface IWebhookRouteCache
{
    bool TryGetRoute(string routeKey, out WebhookRouteInfo route);

    /// <summary>
    /// Replaces the entire route table with a new snapshot. The caller must not mutate
    /// the passed dictionary afterwards — it is read concurrently without locks.
    /// </summary>
    void UpdateRoutes(Dictionary<string, WebhookRouteInfo> routes);
}
