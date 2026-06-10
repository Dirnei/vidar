namespace Vidar.Core.Messages;

public sealed record WebhookReceived(
    string RouteKey,
    Guid PayloadId,
    Dictionary<string, string> Headers,
    string ContentType,
    long ContentLength,
    DateTimeOffset ReceivedAt);
