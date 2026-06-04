using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoRoomRepository : IRoomRepository
{
    private readonly IMongoCollection<RoomConfiguration> _collection;

    public MongoRoomRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<RoomConfiguration>("rooms");
    }

    public async Task<List<RoomConfiguration>> GetAllAsync() =>
        await _collection.Find(Builders<RoomConfiguration>.Filter.Empty).ToListAsync();

    public async Task<RoomConfiguration?> GetByIdAsync(Guid id) =>
        await _collection.Find(Builders<RoomConfiguration>.Filter.Eq(r => r.Id, id))
            .FirstOrDefaultAsync();

    public async Task CreateAsync(RoomConfiguration room) =>
        await _collection.InsertOneAsync(room);

    public async Task UpdateAsync(RoomConfiguration room) =>
        await _collection.ReplaceOneAsync(
            Builders<RoomConfiguration>.Filter.Eq(r => r.Id, room.Id),
            room);

    public async Task DeleteAsync(Guid id) =>
        await _collection.DeleteOneAsync(
            Builders<RoomConfiguration>.Filter.Eq(r => r.Id, id));
}
