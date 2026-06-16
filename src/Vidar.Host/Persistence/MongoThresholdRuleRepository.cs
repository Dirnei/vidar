using MongoDB.Driver;
using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public sealed class MongoThresholdRuleRepository : IThresholdRuleRepository
{
    private readonly IMongoCollection<ThresholdRule> _collection;

    public MongoThresholdRuleRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ThresholdRule>("threshold_rules");
    }

    public async Task<List<ThresholdRule>> GetAllAsync() =>
        await _collection.Find(Builders<ThresholdRule>.Filter.Empty).ToListAsync();

    public async Task<ThresholdRule?> GetByIdAsync(Guid id) =>
        await _collection.Find(Builders<ThresholdRule>.Filter.Eq(r => r.Id, id))
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(ThresholdRule rule) =>
        await _collection.ReplaceOneAsync(
            Builders<ThresholdRule>.Filter.Eq(r => r.Id, rule.Id),
            rule,
            new ReplaceOptions { IsUpsert = true });

    public async Task DeleteAsync(Guid id) =>
        await _collection.DeleteOneAsync(
            Builders<ThresholdRule>.Filter.Eq(r => r.Id, id));
}
