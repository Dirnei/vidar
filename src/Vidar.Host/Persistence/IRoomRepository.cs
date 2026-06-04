using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IRoomRepository
{
    Task<List<RoomConfiguration>> GetAllAsync();
    Task<RoomConfiguration?> GetByIdAsync(Guid id);
    Task CreateAsync(RoomConfiguration room);
    Task UpdateAsync(RoomConfiguration room);
    Task DeleteAsync(Guid id);
}
