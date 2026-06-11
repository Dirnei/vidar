namespace Vidar.Host.Api.Dto;

public sealed record WebhookEventResponse(
    Guid PayloadId,
    string RouteKey,
    string? IntegrationId,
    string ContentType,
    long ContentLength,
    DateTimeOffset ReceivedAt,
    string Status,
    DateTimeOffset? HandledAt,
    string? Error);

public sealed record WebhookEventPageResponse(
    IReadOnlyList<WebhookEventResponse> Items,
    long TotalCount);

public sealed record WebhookHandledResponse(
    Guid PayloadId,
    string Status,
    string? Error,
    DateTimeOffset HandledAt);
