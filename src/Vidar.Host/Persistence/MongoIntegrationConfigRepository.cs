using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoIntegrationConfigRepository : IIntegrationConfigRepository
{
    private readonly IMongoCollection<IntegrationConfig> _collection;

    public MongoIntegrationConfigRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<IntegrationConfig>("integrations");
    }

    public async Task<List<IntegrationConfig>> GetAllAsync() =>
        await _collection.Find(Builders<IntegrationConfig>.Filter.Empty).ToListAsync();

    public async Task<IntegrationConfig?> GetByIdAsync(string id) =>
        await _collection.Find(Builders<IntegrationConfig>.Filter.Eq(c => c.Id, id))
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(IntegrationConfig config) =>
        await _collection.ReplaceOneAsync(
            Builders<IntegrationConfig>.Filter.Eq(c => c.Id, config.Id),
            config,
            new ReplaceOptions { IsUpsert = true });

    public async Task DeleteAsync(string id) =>
        await _collection.DeleteOneAsync(
            Builders<IntegrationConfig>.Filter.Eq(c => c.Id, id));
}
