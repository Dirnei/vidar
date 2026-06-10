using Vidar.Core.Messages;

namespace Vidar.Host.Webhooks;

public sealed record WebhookRouteInfo(WebhookAuthMode AuthMode, string? Secret, string? HeaderName);

public interface IWebhookRouteCache
{
    bool TryGetRoute(string routeKey, out WebhookRouteInfo route);
    void UpdateRoutes(Dictionary<string, WebhookRouteInfo> routes);
}
