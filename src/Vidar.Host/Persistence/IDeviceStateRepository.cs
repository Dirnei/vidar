using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IDeviceStateRepository
{
    Task<DeviceState?> GetByDeviceIdAsync(Guid deviceId);
    Task<List<DeviceState>> GetAllAsync();
    Task UpsertAsync(DeviceState state);
}
