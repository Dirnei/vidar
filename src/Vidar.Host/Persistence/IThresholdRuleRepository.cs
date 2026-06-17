using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IThresholdRuleRepository
{
    Task<List<ThresholdRule>> GetAllAsync();
    Task<ThresholdRule?> GetByIdAsync(Guid id);
    Task UpsertAsync(ThresholdRule rule);
    Task DeleteAsync(Guid id);
}
