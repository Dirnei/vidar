using Vidar.Core.Messages;

namespace Vidar.Host.Api.Dto;

public sealed record WebhookRouteResponse(
    string RouteKey,
    string? IntegrationId,
    WebhookAuthMode AuthMode,
    string Path,          // ready-to-use relative URL; embeds the secret for UrlSecret routes
    string? HeaderName);  // set for HeaderToken routes so the UI can hint at the header
