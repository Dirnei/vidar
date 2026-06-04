using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoDiscoveredDeviceRepository : IDiscoveredDeviceRepository
{
    private readonly IMongoCollection<DiscoveredDevice> _collection;

    public MongoDiscoveredDeviceRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<DiscoveredDevice>("discoveredDevices");
    }

    public async Task<List<DiscoveredDevice>> GetAllAsync() =>
        await _collection.Find(Builders<DiscoveredDevice>.Filter.Empty).ToListAsync();

    public async Task<DiscoveredDevice?> GetByIdAsync(Guid id) =>
        await _collection.Find(Builders<DiscoveredDevice>.Filter.Eq(d => d.Id, id))
            .FirstOrDefaultAsync();

    public async Task<DiscoveredDevice?> GetByNativeIdAsync(string communicationType, string nativeId) =>
        await _collection.Find(
            Builders<DiscoveredDevice>.Filter.And(
                Builders<DiscoveredDevice>.Filter.Eq(d => d.CommunicationType, communicationType),
                Builders<DiscoveredDevice>.Filter.Eq(d => d.NativeId, nativeId)))
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(DiscoveredDevice device) =>
        await _collection.ReplaceOneAsync(
            Builders<DiscoveredDevice>.Filter.Eq(d => d.Id, device.Id),
            device,
            new ReplaceOptions { IsUpsert = true });

    public async Task DeleteAsync(Guid id) =>
        await _collection.DeleteOneAsync(
            Builders<DiscoveredDevice>.Filter.Eq(d => d.Id, id));
}
