using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Capabilities;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Loxone;
using Vidar.Host.Persistence;
using Vidar.Host.Tests.Persistence;
using Xunit;

namespace Vidar.Host.Tests.Loxone;

public class LoxoneRoomMappingTests
{
    private static DiscoveredDevice LoxDevice(string serial, string roomUuid, string roomName) => new()
    {
        Id = Guid.NewGuid(), CommunicationType = "loxone", NativeId = $"{serial}/u{roomUuid}",
        Capabilities = new List<CapabilityDescriptor>(),
        Metadata = new() { ["serial"] = serial, ["loxoneRoomUuid"] = roomUuid, ["loxoneRoomName"] = roomName },
    };

    private sealed class FakeDiscoveredRepo : IDiscoveredDeviceRepository
    {
        private readonly List<DiscoveredDevice> _items;
        public FakeDiscoveredRepo(List<DiscoveredDevice> items) => _items = items;
        public Task<List<DiscoveredDevice>> GetAllAsync() => Task.FromResult(_items.ToList());
        public Task<DiscoveredDevice?> GetByIdAsync(Guid id) =>
            Task.FromResult(_items.FirstOrDefault(d => d.Id == id));
        public Task<DiscoveredDevice?> GetByNativeIdAsync(string communicationType, string nativeId) =>
            Task.FromResult(_items.FirstOrDefault(d => d.CommunicationType == communicationType && d.NativeId == nativeId));
        public Task UpsertAsync(DiscoveredDevice device)
        {
            _items.RemoveAll(d => d.Id == device.Id);
            _items.Add(device);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id) { _items.RemoveAll(d => d.Id == id); return Task.CompletedTask; }
    }

    private sealed class FakeRoomRepo : IRoomRepository
    {
        private readonly List<RoomConfiguration> _items;
        public List<RoomConfiguration> Created { get; } = new();
        public FakeRoomRepo(List<RoomConfiguration>? items = null) => _items = items ?? new();
        public Task<List<RoomConfiguration>> GetAllAsync() => Task.FromResult(_items.ToList());
        public Task<RoomConfiguration?> GetByIdAsync(Guid id) =>
            Task.FromResult(_items.FirstOrDefault(r => r.Id == id));
        public Task CreateAsync(RoomConfiguration room)
        {
            _items.Add(room);
            Created.Add(room);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(RoomConfiguration room) => Task.CompletedTask;
        public Task DeleteAsync(Guid id) { _items.RemoveAll(r => r.Id == id); return Task.CompletedTask; }
    }

    private sealed class FakeDeviceRepo : IDeviceRepository
    {
        private readonly List<DeviceConfiguration> _items;
        public List<DeviceConfiguration> Updated { get; } = new();
        public FakeDeviceRepo(List<DeviceConfiguration>? items = null) => _items = items ?? new();
        public Task<List<DeviceConfiguration>> GetAllAsync() => Task.FromResult(_items.ToList());
        public Task<DeviceConfiguration?> GetByIdAsync(Guid id) =>
            Task.FromResult(_items.FirstOrDefault(d => d.Id == id));
        public Task<List<DeviceConfiguration>> GetByRoomIdAsync(Guid roomId) =>
            Task.FromResult(_items.Where(d => d.RoomId == roomId).ToList());
        public Task CreateAsync(DeviceConfiguration device) { _items.Add(device); return Task.CompletedTask; }
        public Task UpdateAsync(DeviceConfiguration device) { Updated.Add(device); return Task.CompletedTask; }
        public Task DeleteAsync(Guid id) { _items.RemoveAll(d => d.Id == id); return Task.CompletedTask; }
    }

    private static LoxoneController BuildController(
        InMemoryRoomMappingRepository mappings,
        List<DiscoveredDevice>? discovered = null,
        List<DeviceConfiguration>? configured = null,
        List<RoomConfiguration>? rooms = null,
        FakeRoomRepo? roomRepo = null,
        FakeDeviceRepo? deviceRepo = null)
    {
        var sidecar = Substitute.For<ILoxoneSidecar>();
        var configRepo = Substitute.For<IApplicationConfigRepository>();
        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IActorRef>()));

        var discoveredRepo = new FakeDiscoveredRepo(discovered ?? new());
        deviceRepo ??= new FakeDeviceRepo(configured ?? new());
        roomRepo ??= new FakeRoomRepo(rooms ?? new());

        return new LoxoneController(sidecar, configRepo, pluginRegistryProvider, mappings, discoveredRepo, deviceRepo, roomRepo);
    }

    [Fact]
    public async Task GetRooms_returns_distinct_rooms_from_discovered_devices_with_mapping()
    {
        var mappings = new InMemoryRoomMappingRepository();
        var vidarRoom = Guid.NewGuid();
        await mappings.UpsertAsync(new RoomMapping { Id = Guid.NewGuid(), PluginId = "loxone", Serial = "AAA", ExternalRoomId = "r1", ExternalRoomName = "OG Kitchen", VidarRoomId = vidarRoom });

        var controller = BuildController(mappings,
            discovered: [LoxDevice("AAA", "r1", "OG Kitchen"), LoxDevice("AAA", "r1", "OG Kitchen"), LoxDevice("AAA", "r2", "Living")],
            configured: [],
            rooms: [new RoomConfiguration { Id = vidarRoom, Name = "Kitchen" }]);

        var result = Assert.IsType<OkObjectResult>(await controller.GetRooms());
        var rooms = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result.Value).Cast<object>().ToList();
        Assert.Equal(2, rooms.Count); // r1 (deduped) + r2
        // r1 shows the mapped Vidar room name "Kitchen"; r2 unmapped.
    }

    [Fact]
    public async Task PutMapping_createRoom_creates_vidar_room_and_maps()
    {
        var mappings = new InMemoryRoomMappingRepository();
        var rooms = new FakeRoomRepo();
        var controller = BuildController(mappings, discovered: [LoxDevice("AAA", "r1", "OG Kitchen")], configured: [], roomRepo: rooms);

        await controller.PutRoomMapping(new LoxoneRoomMappingRequest("AAA", "r1", "OG Kitchen", null, "Kitchen"));

        var m = await mappings.GetByExternalAsync("loxone", "AAA", "r1");
        Assert.NotNull(m);
        Assert.NotNull(m!.VidarRoomId);
        Assert.Contains(rooms.Created, r => r.Name == "Kitchen" && r.Id == m.VidarRoomId);
    }

    [Fact]
    public async Task PutMapping_reassigns_already_accepted_devices_in_that_room()
    {
        var mappings = new InMemoryRoomMappingRepository();
        var vidarRoom = Guid.NewGuid();
        var accepted = new DeviceConfiguration
        {
            Id = Guid.NewGuid(), Name = "Kitchen Relay", CommunicationType = "loxone", NativeId = "AAA/u1",
            Capabilities = new(), Settings = new() { ["serial"] = "AAA", ["loxoneRoomUuid"] = "r1" },
        };
        var devRepo = new FakeDeviceRepo([accepted]);
        var controller = BuildController(mappings, discovered: [], configured: [accepted], deviceRepo: devRepo,
            rooms: [new RoomConfiguration { Id = vidarRoom, Name = "Kitchen" }]);

        await controller.PutRoomMapping(new LoxoneRoomMappingRequest("AAA", "r1", "OG Kitchen", vidarRoom, null));

        Assert.Equal(vidarRoom, devRepo.Updated.Single().RoomId);
    }
}
