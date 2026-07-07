using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoRoomMappingRepository : IRoomMappingRepository
{
    private readonly IMongoCollection<RoomMapping> _collection;

    public MongoRoomMappingRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<RoomMapping>("room_mappings");
    }

    public async Task<List<RoomMapping>> GetAllAsync() =>
        await _collection.Find(Builders<RoomMapping>.Filter.Empty).ToListAsync();

    public async Task<RoomMapping?> GetByExternalAsync(string pluginId, string serial, string externalRoomId) =>
        await _collection.Find(Match(pluginId, serial, externalRoomId)).FirstOrDefaultAsync();

    public async Task UpsertAsync(RoomMapping mapping) =>
        await _collection.ReplaceOneAsync(
            Match(mapping.PluginId, mapping.Serial, mapping.ExternalRoomId),
            mapping,
            new ReplaceOptions { IsUpsert = true });

    public async Task DeleteAsync(string pluginId, string serial, string externalRoomId) =>
        await _collection.DeleteOneAsync(Match(pluginId, serial, externalRoomId));

    private static FilterDefinition<RoomMapping> Match(string pluginId, string serial, string externalRoomId) =>
        Builders<RoomMapping>.Filter.Where(m =>
            m.PluginId == pluginId && m.Serial == serial && m.ExternalRoomId == externalRoomId);
}
