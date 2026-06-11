namespace Vidar.Host.Persistence;

public sealed record WebhookEventDocument(
    Guid PayloadId,
    string RouteKey,
    string? IntegrationId,
    string ContentType,
    long ContentLength,
    DateTimeOffset ReceivedAt,
    string Status = "pending",
    DateTimeOffset? HandledAt = null,
    string? Error = null);

public sealed record WebhookEventPage(IReadOnlyList<WebhookEventDocument> Items, long TotalCount);

public interface IWebhookEventRepository
{
    Task InsertAsync(WebhookEventDocument doc);
    Task<WebhookEventPage> ListAsync(string? routeKey, int skip, int take);
    Task AcknowledgeAsync(Guid payloadId, string status, string? error, DateTimeOffset handledAt);
}
