namespace Vidar.Core.Messages;

public enum WebhookHandleStatus { Handled, Failed }

public sealed record WebhookHandled(
    Guid PayloadId,
    WebhookHandleStatus Status,
    string? Error,
    DateTimeOffset HandledAt);
