using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoApplicationConfigRepository : IApplicationConfigRepository
{
    private readonly IMongoCollection<ApplicationConfig> _collection;

    public MongoApplicationConfigRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ApplicationConfig>("integrations");
    }

    public async Task<List<ApplicationConfig>> GetAllAsync() =>
        await _collection.Find(Builders<ApplicationConfig>.Filter.Empty).ToListAsync();

    public async Task<ApplicationConfig?> GetByIdAsync(string id) =>
        await _collection.Find(Builders<ApplicationConfig>.Filter.Eq(c => c.Id, id))
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(ApplicationConfig config) =>
        await _collection.ReplaceOneAsync(
            Builders<ApplicationConfig>.Filter.Eq(c => c.Id, config.Id),
            config,
            new ReplaceOptions { IsUpsert = true });

    public async Task DeleteAsync(string id) =>
        await _collection.DeleteOneAsync(
            Builders<ApplicationConfig>.Filter.Eq(c => c.Id, id));
}
