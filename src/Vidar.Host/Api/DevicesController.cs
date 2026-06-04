using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly IDeviceStateRepository _stateRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly IRequiredActor<DeviceTwinRegion> _twinRegion;

    public DevicesController(
        IDeviceRepository deviceRepo,
        IDeviceStateRepository stateRepo,
        IRoomRepository roomRepo,
        IRequiredActor<DeviceTwinRegion> twinRegion)
    {
        _deviceRepo = deviceRepo;
        _stateRepo = stateRepo;
        _roomRepo = roomRepo;
        _twinRegion = twinRegion;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _deviceRepo.GetAllAsync();
        var states = await _stateRepo.GetAllAsync();
        var rooms = await _roomRepo.GetAllAsync();
        var stateMap = states.ToDictionary(s => s.DeviceId);
        var roomMap = rooms.ToDictionary(r => r.Id, r => r.Name);

        var response = devices.Select(d =>
        {
            stateMap.TryGetValue(d.Id, out var state);
            roomMap.TryGetValue(d.RoomId, out var roomName);
            var stateDict = state?.States.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => kvp.Value);
            return new DeviceResponse(d.Id, d.Name, d.RoomId, roomName, d.CommunicationType, d.Capabilities, stateDict);
        }).ToList();

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();

        var state = await _stateRepo.GetByDeviceIdAsync(id);
        var room = await _roomRepo.GetByIdAsync(device.RoomId);
        var stateDict = state?.States.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

        return Ok(new DeviceResponse(
            device.Id, device.Name, device.RoomId, room?.Name,
            device.CommunicationType, device.Capabilities, stateDict));
    }

    [HttpPost("{id:guid}/command")]
    public async Task<IActionResult> SendCommand(Guid id, [FromBody] DeviceCommandRequest request)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();

        var command = new DeviceCommand(id, device.CommunicationType, device.NativeId, request.Capability, request.Value);
        var region = _twinRegion.ActorRef;
        region.Tell(command);
        return Accepted();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ConfigureDeviceRequest request)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();
        device.Name = request.Name;
        device.RoomId = request.RoomId;
        await _deviceRepo.UpdateAsync(device);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();
        await _deviceRepo.DeleteAsync(id);
        return NoContent();
    }
}
