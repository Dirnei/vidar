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
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        IDeviceRepository deviceRepo,
        IDeviceStateRepository stateRepo,
        IRoomRepository roomRepo,
        IRequiredActor<DeviceTwinRegion> twinRegion,
        ILogger<DevicesController> logger)
    {
        _deviceRepo = deviceRepo;
        _stateRepo = stateRepo;
        _roomRepo = roomRepo;
        _twinRegion = twinRegion;
        _logger = logger;
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

        // Unwrap JsonElement to a primitive so it survives Akka serialization
        var value = UnwrapJsonElement(request.Value) ?? request.Value;
        var command = new DeviceCommand(id, device.CommunicationType, device.NativeId, request.Capability, value);
        _logger.LogInformation("Sending command {Capability}={Value} ({Type}) to device {DeviceId}",
            request.Capability, value, value?.GetType().Name ?? "null", id);
        var region = _twinRegion.ActorRef;
        region.Tell(command);
        return Accepted();
    }

    private static object? UnwrapJsonElement(object? value)
    {
        if (value is not System.Text.Json.JsonElement el) return value;
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.String => el.GetString(),
            _ => value
        };
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
