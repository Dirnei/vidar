using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Vidar.Host.Persistence;

public sealed class MongoWebhookPayloadRepository : IWebhookPayloadRepository
{
    private readonly GridFSBucket _bucket;

    public MongoWebhookPayloadRepository(IMongoDatabase database)
    {
        _bucket = new GridFSBucket(database, new GridFSBucketOptions { BucketName = "webhook_payloads" });
    }

    public async Task<Guid> StoreAsync(string routeKey, string contentType, Dictionary<string, string> headers, Stream body)
    {
        var payloadId = Guid.NewGuid();
        await _bucket.UploadFromStreamAsync(payloadId.ToString("N"), body, new GridFSUploadOptions
        {
            Metadata = new BsonDocument
            {
                ["routeKey"] = routeKey,
                ["contentType"] = contentType,
                ["headers"] = new BsonDocument(
                    headers
                        .GroupBy(h => SanitizeKey(h.Key))
                        .Select(g => new BsonElement(g.Key, g.Last().Value)))
            }
        });
        return payloadId;
    }

    public async Task<WebhookPayload?> OpenAsync(Guid payloadId)
    {
        var file = await FindByPayloadIdAsync(payloadId);
        if (file == null)
            return null;

        try
        {
            var stream = await _bucket.OpenDownloadStreamAsync(file.Id);
            var contentType = file.Metadata != null && file.Metadata.TryGetValue("contentType", out var ct)
                ? ct.AsString
                : "application/octet-stream";
            return new WebhookPayload(stream, contentType);
        }
        catch (GridFSFileNotFoundException)
        {
            // Deleted between lookup and open (e.g. retention sweep) — treat as missing.
            return null;
        }
    }

    public async Task<long> DeleteOlderThanAsync(DateTime cutoffUtc)
    {
        // GridFS chunks must be deleted via the bucket API; a plain TTL index on the
        // files collection would orphan the chunks collection.
        var filter = Builders<GridFSFileInfo>.Filter.Lt(f => f.UploadDateTime, cutoffUtc);
        using var cursor = await _bucket.FindAsync(filter);
        long deleted = 0;
        while (await cursor.MoveNextAsync())
        {
            foreach (var file in cursor.Current)
            {
                await _bucket.DeleteAsync(file.Id);
                deleted++;
            }
        }
        return deleted;
    }

    private async Task<GridFSFileInfo?> FindByPayloadIdAsync(Guid payloadId)
    {
        var filter = Builders<GridFSFileInfo>.Filter.Eq(f => f.Filename, payloadId.ToString("N"));
        using var cursor = await _bucket.FindAsync(filter);
        return await cursor.FirstOrDefaultAsync();
    }

    // BSON element names must not contain '.' — HTTP header names can't either,
    // but defend against malicious input.
    private static string SanitizeKey(string key) => key.Replace('.', '_');
}
