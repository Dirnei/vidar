namespace Vidar.Host.Persistence;

public sealed record WebhookPayload(Stream Content, string ContentType);

public interface IWebhookPayloadRepository
{
    /// <summary>Stores the body stream and returns the generated payload id.</summary>
    Task<Guid> StoreAsync(string routeKey, string contentType, Dictionary<string, string> headers, Stream body);

    /// <summary>Opens the stored payload for streaming, or null if missing/expired.</summary>
    Task<WebhookPayload?> OpenAsync(Guid payloadId);

    /// <summary>Deletes payloads uploaded before the cutoff. Returns the number deleted.</summary>
    Task<long> DeleteOlderThanAsync(DateTime cutoffUtc);
}
