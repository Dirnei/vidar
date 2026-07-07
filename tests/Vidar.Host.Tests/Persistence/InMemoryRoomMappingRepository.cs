using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Persistence;

public sealed class InMemoryRoomMappingRepository : IRoomMappingRepository
{
    private readonly List<RoomMapping> _items = new();

    public Task<List<RoomMapping>> GetAllAsync() => Task.FromResult(_items.ToList());

    public Task<RoomMapping?> GetByExternalAsync(string pluginId, string serial, string externalRoomId) =>
        Task.FromResult(_items.FirstOrDefault(m =>
            m.PluginId == pluginId && m.Serial == serial && m.ExternalRoomId == externalRoomId));

    public Task UpsertAsync(RoomMapping mapping)
    {
        _items.RemoveAll(m => m.PluginId == mapping.PluginId && m.Serial == mapping.Serial
            && m.ExternalRoomId == mapping.ExternalRoomId);
        _items.Add(mapping);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string pluginId, string serial, string externalRoomId)
    {
        _items.RemoveAll(m => m.PluginId == pluginId && m.Serial == serial && m.ExternalRoomId == externalRoomId);
        return Task.CompletedTask;
    }
}
