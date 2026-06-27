using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/devices/discovered")]
public sealed class DiscoveredDevicesController : ControllerBase
{
    private readonly IDiscoveredDeviceRepository _discoveredRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;

    public DiscoveredDevicesController(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo, IRequiredActor<PluginRegistry> pluginRegistryProvider)
    {
        _discoveredRepo = discoveredRepo;
        _deviceRepo = deviceRepo;
        _pluginRegistryProvider = pluginRegistryProvider;
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

        // Merge optional per-device settings overlay (e.g. local IP for Dyson)
        if (request.Settings is not null)
            foreach (var kv in request.Settings)
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

        // Route registration to the appropriate plugin via PluginRegistry
        var pluginRegistry = await _pluginRegistryProvider.GetAsync();

        var host = discovered.Metadata.GetValueOrDefault("host", "");
        var friendlyName = discovered.Metadata.GetValueOrDefault("friendly_name", device.NativeId);
        int.TryParse(discovered.Metadata.GetValueOrDefault("generation", "0"), out var generation);

        var reg = new RegisterDeviceForPolling(
            device.Id, device.CommunicationType, device.NativeId,
            discovered.CommunicationType == "shelly" ? host :
            discovered.CommunicationType == "dyson" ? device.Settings.GetValueOrDefault("ip", "") :
            friendlyName,
            generation, device.Capabilities);

        pluginRegistry.Tell(new RouteToPlugin(discovered.CommunicationType, reg));

        return Created($"/api/devices/{device.Id}", new DeviceResponse(
            device.Id, device.Name, device.RoomId, null,
            device.CommunicationType, device.Capabilities, null, true, device.Settings,
            null, null));
    }
}
