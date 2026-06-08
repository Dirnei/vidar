using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IApplicationConfigRepository
{
    Task<List<ApplicationConfig>> GetAllAsync();
    Task<ApplicationConfig?> GetByIdAsync(string id);
    Task UpsertAsync(ApplicationConfig config);
    Task DeleteAsync(string id);
}
