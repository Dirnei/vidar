using Akka.Actor;
using Akka.Hosting;
using NSubstitute;
using Vidar.Core.Capabilities;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;
using Vidar.Host.Tests.Persistence;
using Xunit;

namespace Vidar.Host.Tests.Api;

public class DiscoveredDevicesRoomMappingTests
{
    [Fact]
    public async Task Configure_without_explicit_room_applies_mapping()
    {
        var mappingRepo = new InMemoryRoomMappingRepository();
        var vidarRoom = Guid.NewGuid();
        await mappingRepo.UpsertAsync(new RoomMapping { Id = Guid.NewGuid(), PluginId = "loxone", Serial = "AAA", ExternalRoomId = "r1", ExternalRoomName = "OG Kitchen", VidarRoomId = vidarRoom });

        var discovered = new DiscoveredDevice
        {
            Id = Guid.NewGuid(), CommunicationType = "loxone", NativeId = "AAA/u1",
            Capabilities = new List<CapabilityDescriptor>(),
            Metadata = new() { ["serial"] = "AAA", ["loxoneRoomUuid"] = "r1", ["loxoneRoomName"] = "OG Kitchen" },
        };
        var deviceRepo = new FakeDeviceRepo();
        var controller = BuildController(discovered, deviceRepo, mappingRepo);

        await controller.Configure(discovered.Id, new ConfigureDeviceRequest("Kitchen Relay", Guid.Empty));

        Assert.Equal(vidarRoom, deviceRepo.Created.Single().RoomId);
    }

    [Fact]
    public async Task Explicit_room_id_overrides_mapping()
    {
        var mappingRepo = new InMemoryRoomMappingRepository();
        await mappingRepo.UpsertAsync(new RoomMapping { Id = Guid.NewGuid(), PluginId = "loxone", Serial = "AAA", ExternalRoomId = "r1", ExternalRoomName = "OG Kitchen", VidarRoomId = Guid.NewGuid() });
        var explicitRoom = Guid.NewGuid();
        var discovered = new DiscoveredDevice
        {
            Id = Guid.NewGuid(), CommunicationType = "loxone", NativeId = "AAA/u1",
            Capabilities = new List<CapabilityDescriptor>(),
            Metadata = new() { ["serial"] = "AAA", ["loxoneRoomUuid"] = "r1" },
        };
        var deviceRepo = new FakeDeviceRepo();
        var controller = BuildController(discovered, deviceRepo, mappingRepo);

        await controller.Configure(discovered.Id, new ConfigureDeviceRequest("x", explicitRoom));

        Assert.Equal(explicitRoom, deviceRepo.Created.Single().RoomId);
    }

    private static DiscoveredDevicesController BuildController(
        DiscoveredDevice discovered, FakeDeviceRepo deviceRepo, IRoomMappingRepository mappingRepo)
    {
        var discoveredRepo = new FakeDiscoveredRepo();
        discoveredRepo.Add(discovered);

        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));

        return new DiscoveredDevicesController(discoveredRepo, deviceRepo, pluginRegistryProvider, mappingRepo);
    }

    private sealed class FakeDiscoveredRepo : IDiscoveredDeviceRepository
    {
        private readonly Dictionary<Guid, DiscoveredDevice> _store = new();

        public void Add(DiscoveredDevice device) => _store[device.Id] = device;

        public Task<List<DiscoveredDevice>> GetAllAsync() =>
            Task.FromResult(_store.Values.ToList());

        public Task<DiscoveredDevice?> GetByIdAsync(Guid id) =>
            Task.FromResult(_store.GetValueOrDefault(id));

        public Task<DiscoveredDevice?> GetByNativeIdAsync(string communicationType, string nativeId) =>
            Task.FromResult(_store.Values.FirstOrDefault(d =>
                d.CommunicationType == communicationType && d.NativeId == nativeId));

        public Task UpsertAsync(DiscoveredDevice device)
        {
            _store[device.Id] = device;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            _store.Remove(id);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeDeviceRepo : IDeviceRepository
    {
        public List<DeviceConfiguration> Created { get; } = new();

        public Task<List<DeviceConfiguration>> GetAllAsync() =>
            Task.FromResult(new List<DeviceConfiguration>());

        public Task<DeviceConfiguration?> GetByIdAsync(Guid id) =>
            Task.FromResult<DeviceConfiguration?>(null);

        public Task<List<DeviceConfiguration>> GetByRoomIdAsync(Guid roomId) =>
            Task.FromResult(new List<DeviceConfiguration>());

        public Task CreateAsync(DeviceConfiguration device)
        {
            Created.Add(device);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DeviceConfiguration device) => Task.CompletedTask;

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }
}
