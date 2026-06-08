using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoHistoryRepository : IHistoryRepository
{
    private readonly IMongoCollection<StateHistoryEntry> _stateCollection;
    private readonly IMongoCollection<CommandHistoryEntry> _commandCollection;

    public MongoHistoryRepository(IMongoDatabase database)
    {
        _stateCollection = database.GetCollection<StateHistoryEntry>("stateHistory");
        _commandCollection = database.GetCollection<CommandHistoryEntry>("commandHistory");

        // Compound index on (DeviceId, Timestamp desc) for efficient paging queries
        _stateCollection.Indexes.CreateOne(
            new CreateIndexModel<StateHistoryEntry>(
                Builders<StateHistoryEntry>.IndexKeys
                    .Ascending(e => e.DeviceId)
                    .Descending(e => e.Timestamp)));

        _commandCollection.Indexes.CreateOne(
            new CreateIndexModel<CommandHistoryEntry>(
                Builders<CommandHistoryEntry>.IndexKeys
                    .Ascending(e => e.DeviceId)
                    .Descending(e => e.Timestamp)));
    }

    public Task AddStateEntryAsync(StateHistoryEntry entry) =>
        _stateCollection.InsertOneAsync(entry);

    public Task AddCommandEntryAsync(CommandHistoryEntry entry) =>
        _commandCollection.InsertOneAsync(entry);

    public async Task<List<StateHistoryEntry>> GetStateHistoryAsync(
        Guid deviceId, int skip = 0, int limit = 20, DateTime? from = null, DateTime? to = null)
    {
        var filter = Builders<StateHistoryEntry>.Filter.Eq(e => e.DeviceId, deviceId);
        if (from.HasValue)
            filter &= Builders<StateHistoryEntry>.Filter.Gte(e => e.Timestamp, from.Value);
        if (to.HasValue)
            filter &= Builders<StateHistoryEntry>.Filter.Lte(e => e.Timestamp, to.Value);

        return await _stateCollection
            .Find(filter)
            .SortByDescending(e => e.Timestamp)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<CommandHistoryEntry>> GetCommandHistoryAsync(
        Guid deviceId, int skip = 0, int limit = 20, DateTime? from = null, DateTime? to = null)
    {
        var filter = Builders<CommandHistoryEntry>.Filter.Eq(e => e.DeviceId, deviceId);
        if (from.HasValue)
            filter &= Builders<CommandHistoryEntry>.Filter.Gte(e => e.Timestamp, from.Value);
        if (to.HasValue)
            filter &= Builders<CommandHistoryEntry>.Filter.Lte(e => e.Timestamp, to.Value);

        return await _commandCollection
            .Find(filter)
            .SortByDescending(e => e.Timestamp)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();
    }
}
