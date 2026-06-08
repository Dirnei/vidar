using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/devices/discovered")]
public sealed class DiscoveredDevicesController : ControllerBase
{
    private readonly IDiscoveredDeviceRepository _discoveredRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly ActorSystem _actorSystem;

    public DiscoveredDevicesController(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo, ActorSystem actorSystem)
    {
        _discoveredRepo = discoveredRepo;
        _deviceRepo = deviceRepo;
        _actorSystem = actorSystem;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var discovered = await _discoveredRepo.GetAllAsync();
        var configured = await _deviceRepo.GetAllAsync();
        var configuredNativeIds = configured.Select(d => d.NativeId).ToHashSet();

        var response = discovered
            .Where(d => !configuredNativeIds.Contains(d.NativeId))
            .Select(d => new DiscoveredDeviceResponse(
                d.Id, d.CommunicationType, d.NativeId, d.Capabilities, d.Metadata, d.DiscoveredAt))
            .ToList();
        return Ok(response);
    }

    [HttpPost("{id:guid}/configure")]
    public async Task<IActionResult> Configure(Guid id, [FromBody] ConfigureDeviceRequest request)
    {
        var discovered = await _discoveredRepo.GetByIdAsync(id);
        if (discovered == null) return NotFound();

        // Copy relevant metadata into Settings so it is available after the DiscoveredDevice is deleted
        var settings = new Dictionary<string, string>();
        foreach (var kv in discovered.Metadata)
            settings[kv.Key] = kv.Value;

        var device = new DeviceConfiguration
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            RoomId = request.RoomId,
            CommunicationType = discovered.CommunicationType,
            NativeId = discovered.NativeId,
            Capabilities = discovered.Capabilities,
            Settings = settings
        };

        await _deviceRepo.CreateAsync(device);
        await _discoveredRepo.DeleteAsync(id);

        // Publish registration so the appropriate communication node can start polling
        var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
        if (discovered.CommunicationType == "shelly" &&
            discovered.Metadata.TryGetValue("host", out var host))
        {
            int.TryParse(discovered.Metadata.GetValueOrDefault("generation", "2"), out var generation);
            mediator.Tell(new Publish("register.shelly", new RegisterDeviceForPolling(
                device.Id, device.CommunicationType, device.NativeId, host, generation, device.Capabilities)));
        }
        else if (discovered.CommunicationType == "zigbee2mqtt")
        {
            var friendlyName = discovered.Metadata.GetValueOrDefault("friendly_name", device.NativeId);
            mediator.Tell(new Publish("register.zigbee2mqtt", new RegisterDeviceForPolling(
                device.Id, device.CommunicationType, device.NativeId, friendlyName, 0, device.Capabilities)));
        }
        else if (discovered.CommunicationType == "unifi")
        {
            mediator.Tell(new Publish("register.unifi", new RegisterDeviceForPolling(
                device.Id, device.CommunicationType, device.NativeId, "", 0, device.Capabilities)));
        }

        return Created($"/api/devices/{device.Id}", new DeviceResponse(
            device.Id, device.Name, device.RoomId, null,
            device.CommunicationType, device.Capabilities, null, true, device.Settings,
            null, null));
    }
}
