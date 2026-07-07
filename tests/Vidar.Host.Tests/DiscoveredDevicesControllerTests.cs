using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Capabilities;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests;

public class DiscoveredDevicesControllerTests
{
    [Fact]
    public async Task Configure_MergesSettingsOverlay_AddsIp()
    {
        // Factory builds the controller with in-memory discovered+device repos and a
        // PluginRegistry probe; seeds one discovered "dyson" device with metadata
        // {serial, productType, mqttPassword}.
        var (controller, deviceRepo, discoveredId) = DiscoveredControllerTestFactory.SeedDyson();

        var result = await controller.Configure(discoveredId, new ConfigureDeviceRequest("Bedroom Dyson", Guid.Empty)
        {
            Settings = new() { ["ip"] = "192.168.5.157" },
        });

        Assert.IsType<CreatedResult>(result);
        var saved = deviceRepo.LastCreated!;
        Assert.Equal("192.168.5.157", saved.Settings["ip"]);
        Assert.True(saved.Settings.ContainsKey("mqttPassword")); // metadata still copied
    }
}

internal static class DiscoveredControllerTestFactory
{
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
        public DeviceConfiguration? LastCreated { get; private set; }

        public Task<List<DeviceConfiguration>> GetAllAsync() =>
            Task.FromResult(new List<DeviceConfiguration>());

        public Task<DeviceConfiguration?> GetByIdAsync(Guid id) =>
            Task.FromResult<DeviceConfiguration?>(null);

        public Task<List<DeviceConfiguration>> GetByRoomIdAsync(Guid roomId) =>
            Task.FromResult(new List<DeviceConfiguration>());

        public Task CreateAsync(DeviceConfiguration device)
        {
            LastCreated = device;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DeviceConfiguration device) => Task.CompletedTask;

        public Task DeleteAsync(Guid id) => Task.CompletedTask;
    }

    public static (DiscoveredDevicesController controller, FakeDeviceRepo deviceRepo, Guid discoveredId) SeedDyson()
    {
        var discoveredId = Guid.NewGuid();
        var discoveredDevice = new DiscoveredDevice
        {
            Id = discoveredId,
            CommunicationType = "dyson",
            NativeId = "X6P-EU-SKA0802A",
            Capabilities = new List<CapabilityDescriptor>(),
            Metadata = new Dictionary<string, string>
            {
                ["serial"] = "X6P-EU-SKA0802A",
                ["productType"] = "358K",
                ["mqttPassword"] = "secret-pw",
            },
            DiscoveredAt = DateTime.UtcNow,
        };

        var discoveredRepo = new FakeDiscoveredRepo();
        discoveredRepo.Add(discoveredDevice);

        var deviceRepo = new FakeDeviceRepo();

        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));

        var mappingRepo = new Vidar.Host.Tests.Persistence.InMemoryRoomMappingRepository();
        var controller = new DiscoveredDevicesController(discoveredRepo, deviceRepo, pluginRegistryProvider, mappingRepo);

        return (controller, deviceRepo, discoveredId);
    }
}
