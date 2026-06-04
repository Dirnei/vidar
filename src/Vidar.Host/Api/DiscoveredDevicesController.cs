using Microsoft.AspNetCore.Mvc;
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

    public DiscoveredDevicesController(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo)
    {
        _discoveredRepo = discoveredRepo;
        _deviceRepo = deviceRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _discoveredRepo.GetAllAsync();
        var response = devices.Select(d => new DiscoveredDeviceResponse(
            d.Id, d.CommunicationType, d.NativeId, d.Capabilities, d.Metadata, d.DiscoveredAt)).ToList();
        return Ok(response);
    }

    [HttpPost("{id:guid}/configure")]
    public async Task<IActionResult> Configure(Guid id, [FromBody] ConfigureDeviceRequest request)
    {
        var discovered = await _discoveredRepo.GetByIdAsync(id);
        if (discovered == null) return NotFound();

        var device = new DeviceConfiguration
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            RoomId = request.RoomId,
            CommunicationType = discovered.CommunicationType,
            NativeId = discovered.NativeId,
            Capabilities = discovered.Capabilities
        };

        await _deviceRepo.CreateAsync(device);
        await _discoveredRepo.DeleteAsync(id);
        return Created($"/api/devices/{device.Id}", new DeviceResponse(
            device.Id, device.Name, device.RoomId, null,
            device.CommunicationType, device.Capabilities, null));
    }
}
