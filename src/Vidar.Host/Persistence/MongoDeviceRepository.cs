using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoDeviceRepository : IDeviceRepository
{
    private readonly IMongoCollection<DeviceConfiguration> _collection;

    public MongoDeviceRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<DeviceConfiguration>("devices");
    }

    public async Task<List<DeviceConfiguration>> GetAllAsync() =>
        await _collection.Find(Builders<DeviceConfiguration>.Filter.Empty).ToListAsync();

    public async Task<DeviceConfiguration?> GetByIdAsync(Guid id) =>
        await _collection.Find(Builders<DeviceConfiguration>.Filter.Eq(d => d.Id, id))
            .FirstOrDefaultAsync();

    public async Task<List<DeviceConfiguration>> GetByRoomIdAsync(Guid roomId) =>
        await _collection.Find(Builders<DeviceConfiguration>.Filter.Eq(d => d.RoomId, roomId))
            .ToListAsync();

    public async Task CreateAsync(DeviceConfiguration device) =>
        await _collection.InsertOneAsync(device);

    public async Task UpdateAsync(DeviceConfiguration device) =>
        await _collection.ReplaceOneAsync(
            Builders<DeviceConfiguration>.Filter.Eq(d => d.Id, device.Id),
            device);

    public async Task DeleteAsync(Guid id) =>
        await _collection.DeleteOneAsync(
            Builders<DeviceConfiguration>.Filter.Eq(d => d.Id, id));
}
