using Vidar.Core.Model;

namespace Vidar.Host.Persistence;

public interface IRoomMappingRepository
{
    Task<List<RoomMapping>> GetAllAsync();
    Task<RoomMapping?> GetByExternalAsync(string pluginId, string serial, string externalRoomId);
    Task UpsertAsync(RoomMapping mapping);
    Task DeleteAsync(string pluginId, string serial, string externalRoomId);
}
