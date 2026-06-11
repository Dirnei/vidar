using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Vidar.Host.Persistence;

public sealed class MongoWebhookEventRepository : IWebhookEventRepository
{
    private readonly IMongoCollection<WebhookEventBson> _collection;

    public MongoWebhookEventRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<WebhookEventBson>("webhook_events");
        var indexKeys = Builders<WebhookEventBson>.IndexKeys;
        _collection.Indexes.CreateMany([
            new CreateIndexModel<WebhookEventBson>(
                indexKeys.Ascending(e => e.ReceivedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromHours(24), Name = "ttl_receivedAt" }),
            new CreateIndexModel<WebhookEventBson>(
                indexKeys.Ascending(e => e.RouteKey).Descending(e => e.ReceivedAt),
                new CreateIndexOptions { Name = "routeKey_receivedAt" }),
        ]);
    }

    public async Task InsertAsync(WebhookEventDocument doc)
    {
        await _collection.InsertOneAsync(new WebhookEventBson
        {
            PayloadId = doc.PayloadId,
            RouteKey = doc.RouteKey,
            IntegrationId = doc.IntegrationId,
            ContentType = doc.ContentType,
            ContentLength = doc.ContentLength,
            ReceivedAt = doc.ReceivedAt.UtcDateTime,
        });
    }

    public async Task<WebhookEventPage> ListAsync(string? routeKey, int skip, int take)
    {
        var filter = routeKey != null
            ? Builders<WebhookEventBson>.Filter.Eq(e => e.RouteKey, routeKey)
            : Builders<WebhookEventBson>.Filter.Empty;
        var sort = Builders<WebhookEventBson>.Sort.Descending(e => e.ReceivedAt);

        var totalTask = _collection.CountDocumentsAsync(filter);
        var itemsTask = _collection.Find(filter).Sort(sort).Skip(skip).Limit(take).ToListAsync();
        await Task.WhenAll(totalTask, itemsTask);

        var items = itemsTask.Result.Select(e => new WebhookEventDocument(
            e.PayloadId, e.RouteKey, e.IntegrationId, e.ContentType, e.ContentLength,
            new DateTimeOffset(e.ReceivedAt, TimeSpan.Zero),
            e.Status,
            e.HandledAt.HasValue ? new DateTimeOffset(e.HandledAt.Value, TimeSpan.Zero) : null,
            e.Error)).ToList();

        return new WebhookEventPage(items, totalTask.Result);
    }

    public async Task AcknowledgeAsync(Guid payloadId, string status, string? error, DateTimeOffset handledAt)
    {
        var filter = Builders<WebhookEventBson>.Filter.Eq(e => e.PayloadId, payloadId);
        var update = Builders<WebhookEventBson>.Update
            .Set(e => e.Status, status)
            .Set(e => e.HandledAt, handledAt.UtcDateTime)
            .Set(e => e.Error, error);
        await _collection.UpdateOneAsync(filter, update);
    }

    [BsonIgnoreExtraElements]
    private sealed class WebhookEventBson
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public Guid PayloadId { get; set; }
        public string RouteKey { get; set; } = "";
        public string? IntegrationId { get; set; }
        public string ContentType { get; set; } = "";
        public long ContentLength { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime? HandledAt { get; set; }
        public string? Error { get; set; }
    }
}
