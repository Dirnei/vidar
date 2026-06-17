using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoThresholdEventLogRepository : IThresholdEventLogRepository
{
    private readonly IMongoCollection<ThresholdEventLog> _collection;

    public MongoThresholdEventLogRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ThresholdEventLog>("threshold_event_log");
    }

    public async Task InsertAsync(ThresholdEventLog entry) =>
        await _collection.InsertOneAsync(entry);

    public async Task<List<ThresholdEventLog>> GetRecentAsync(int skip = 0, int limit = 50) =>
        await _collection
            .Find(Builders<ThresholdEventLog>.Filter.Empty)
            .SortByDescending(e => e.FiredAt)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();

    public async Task<long> CountAsync() =>
        await _collection.CountDocumentsAsync(Builders<ThresholdEventLog>.Filter.Empty);
}
