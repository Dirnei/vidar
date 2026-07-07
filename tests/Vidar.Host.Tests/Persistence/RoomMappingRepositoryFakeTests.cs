using Vidar.Core.Model;
using Xunit;

namespace Vidar.Host.Tests.Persistence;

public class RoomMappingRepositoryFakeTests
{
    [Fact]
    public async Task Upsert_then_get_by_external_returns_mapping_and_dedupes()
    {
        var repo = new InMemoryRoomMappingRepository();
        var vidarRoom = Guid.NewGuid();
        await repo.UpsertAsync(new RoomMapping { Id = Guid.NewGuid(), PluginId = "loxone", Serial = "AAA", ExternalRoomId = "r1", ExternalRoomName = "OG Kitchen", VidarRoomId = vidarRoom });
        await repo.UpsertAsync(new RoomMapping { Id = Guid.NewGuid(), PluginId = "loxone", Serial = "AAA", ExternalRoomId = "r1", ExternalRoomName = "OG Kitchen", VidarRoomId = null });

        var all = await repo.GetAllAsync();
        Assert.Single(all); // upsert dedupes on (plugin,serial,external)
        var m = await repo.GetByExternalAsync("loxone", "AAA", "r1");
        Assert.NotNull(m);
        Assert.Null(m!.VidarRoomId); // second upsert (unmap) won

        await repo.DeleteAsync("loxone", "AAA", "r1");
        Assert.Empty(await repo.GetAllAsync());
    }
}
