using Vidar.Core.Messages;

namespace Vidar.Host.Webhooks;

public sealed record WebhookRouteInfo(
    WebhookAuthMode AuthMode, string? Secret, string? HeaderName, string? IntegrationId = null);

public interface IWebhookRouteCache
{
    bool TryGetRoute(string routeKey, out WebhookRouteInfo route);

    /// <summary>Current route table snapshot for read-only enumeration (e.g. the routes API).</summary>
    IReadOnlyDictionary<string, WebhookRouteInfo> Snapshot();

    /// <summary>
    /// Replaces the entire route table with a new snapshot. The caller must not mutate
    /// the passed dictionary afterwards — it is read concurrently without locks.
    /// </summary>
    void UpdateRoutes(Dictionary<string, WebhookRouteInfo> routes);
}
