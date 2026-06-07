using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IGroupRepository
{
    Task<List<GroupConfiguration>> GetAllAsync();
    Task<GroupConfiguration?> GetByIdAsync(Guid id);
    Task<List<GroupConfiguration>> GetByRoomIdAsync(Guid roomId);
    Task CreateAsync(GroupConfiguration group);
    Task UpdateAsync(GroupConfiguration group);
    Task DeleteAsync(Guid id);
}
