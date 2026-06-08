using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IIntegrationConfigRepository
{
    Task<List<IntegrationConfig>> GetAllAsync();
    Task<IntegrationConfig?> GetByIdAsync(string id);
    Task UpsertAsync(IntegrationConfig config);
    Task DeleteAsync(string id);
}
