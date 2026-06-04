using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IDiscoveredDeviceRepository
{
    Task<List<DiscoveredDevice>> GetAllAsync();
    Task<DiscoveredDevice?> GetByIdAsync(Guid id);
    Task<DiscoveredDevice?> GetByNativeIdAsync(string communicationType, string nativeId);
    Task UpsertAsync(DiscoveredDevice device);
    Task DeleteAsync(Guid id);
}
