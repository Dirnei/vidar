using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoDeviceStateRepository : IDeviceStateRepository
{
    private readonly IMongoCollection<DeviceState> _collection;

    public MongoDeviceStateRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<DeviceState>("deviceState");
    }

    public async Task<DeviceState?> GetByDeviceIdAsync(Guid deviceId) =>
        await _collection.Find(Builders<DeviceState>.Filter.Eq(s => s.DeviceId, deviceId))
            .FirstOrDefaultAsync();

    public async Task<List<DeviceState>> GetAllAsync() =>
        await _collection.Find(Builders<DeviceState>.Filter.Empty).ToListAsync();

    public async Task UpsertAsync(DeviceState state) =>
        await _collection.ReplaceOneAsync(
            Builders<DeviceState>.Filter.Eq(s => s.DeviceId, state.DeviceId),
            state,
            new ReplaceOptions { IsUpsert = true });
}
