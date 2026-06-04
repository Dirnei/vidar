using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IDeviceRepository
{
    Task<List<DeviceConfiguration>> GetAllAsync();
    Task<DeviceConfiguration?> GetByIdAsync(Guid id);
    Task<List<DeviceConfiguration>> GetByRoomIdAsync(Guid roomId);
    Task CreateAsync(DeviceConfiguration device);
    Task UpdateAsync(DeviceConfiguration device);
    Task DeleteAsync(Guid id);
}
