using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoGroupRepository : IGroupRepository
{
    private readonly IMongoCollection<GroupConfiguration> _collection;

    public MongoGroupRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<GroupConfiguration>("groups");
    }

    public async Task<List<GroupConfiguration>> GetAllAsync() =>
        await _collection.Find(Builders<GroupConfiguration>.Filter.Empty).ToListAsync();

    public async Task<GroupConfiguration?> GetByIdAsync(Guid id) =>
        await _collection.Find(Builders<GroupConfiguration>.Filter.Eq(g => g.Id, id))
            .FirstOrDefaultAsync();

    public async Task<List<GroupConfiguration>> GetByRoomIdAsync(Guid roomId) =>
        await _collection.Find(Builders<GroupConfiguration>.Filter.Eq(g => g.RoomId, roomId))
            .ToListAsync();

    public async Task CreateAsync(GroupConfiguration group) =>
        await _collection.InsertOneAsync(group);

    public async Task UpdateAsync(GroupConfiguration group) =>
        await _collection.ReplaceOneAsync(
            Builders<GroupConfiguration>.Filter.Eq(g => g.Id, group.Id),
            group);

    public async Task DeleteAsync(Guid id) =>
        await _collection.DeleteOneAsync(
            Builders<GroupConfiguration>.Filter.Eq(g => g.Id, id));
}
