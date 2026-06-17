using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
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
    private readonly IGroupRepository _groupRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly IRequiredActor<DeviceTwinRegion> _twinRegion;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        IDeviceRepository deviceRepo,
        IDeviceStateRepository stateRepo,
        IRoomRepository roomRepo,
        IGroupRepository groupRepo,
        IHistoryRepository historyRepo,
        IRequiredActor<DeviceTwinRegion> twinRegion,
        IRequiredActor<PluginRegistry> pluginRegistryProvider,
        ILogger<DevicesController> logger)
    {
        _deviceRepo = deviceRepo;
        _stateRepo = stateRepo;
        _roomRepo = roomRepo;
        _groupRepo = groupRepo;
        _historyRepo = historyRepo;
        _twinRegion = twinRegion;
        _pluginRegistryProvider = pluginRegistryProvider;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var devices = await _deviceRepo.GetAllAsync();
        var states = await _stateRepo.GetAllAsync();
        var rooms = await _roomRepo.GetAllAsync();
        var groups = await _groupRepo.GetAllAsync();
        var stateMap = states.ToDictionary(s => s.DeviceId);
        var roomMap = rooms.ToDictionary(r => r.Id, r => r.Name);
        // Build deviceId -> (groupId, groupName) lookup
        var deviceGroupMap = new Dictionary<Guid, (Guid GroupId, string GroupName)>();
        foreach (var g in groups)
        {
            foreach (var deviceId in g.DeviceIds)
                deviceGroupMap[deviceId] = (g.Id, g.Name);
        }

        var response = devices.Select(d =>
        {
            stateMap.TryGetValue(d.Id, out var state);
            roomMap.TryGetValue(d.RoomId, out var roomName);
            deviceGroupMap.TryGetValue(d.Id, out var groupInfo);
            var stateDict = state?.States;
            return new DeviceResponse(d.Id, d.Name, d.RoomId, roomName, d.CommunicationType, d.Capabilities, stateDict, state?.Online, d.Settings,
                groupInfo.GroupId == Guid.Empty ? null : groupInfo.GroupId,
                groupInfo.GroupName);
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
        var stateDict = state?.States;

        var groups = await _groupRepo.GetAllAsync();
        var group = groups.FirstOrDefault(g => g.DeviceIds.Contains(id));

        return Ok(new DeviceResponse(
            device.Id, device.Name, device.RoomId, room?.Name,
            device.CommunicationType, device.Capabilities, stateDict, state?.Online, device.Settings,
            group?.Id, group?.Name));
    }

    [HttpPost("{id:guid}/command")]
    public async Task<IActionResult> SendCommand(Guid id, [FromBody] DeviceCommandRequest request)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();

        // Unwrap JsonElement to a primitive so it survives Akka serialization
        var value = UnwrapJsonElement(request.Value) ?? request.Value;
        var command = new DeviceCommand(id, device.CommunicationType, device.NativeId, request.CapabilityKey, value);
        _logger.LogInformation("Sending command {Capability}={Value} ({Type}) to device {DeviceId}",
            request.CapabilityKey, value, value?.GetType().Name ?? "null", id);
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

    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] UpdateDeviceSettingsRequest request)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();

        var oldHost = device.Settings.GetValueOrDefault("host");

        foreach (var kvp in request.Settings)
            device.Settings[kvp.Key] = kvp.Value;

        await _deviceRepo.UpdateAsync(device);

        var newHost = device.Settings.GetValueOrDefault("host");
        if (newHost != null && newHost != oldHost && device.CommunicationType == "shelly")
        {
            int.TryParse(device.Settings.GetValueOrDefault("generation", "2"), out var generation);
            var msg = new RegisterDeviceForPolling(
                device.Id,
                device.CommunicationType,
                device.NativeId,
                newHost,
                generation,
                device.Capabilities);
            var pluginRegistry = await _pluginRegistryProvider.GetAsync();
            pluginRegistry.Tell(new RouteToPlugin(device.CommunicationType, msg));
            _logger.LogInformation("Republished RegisterDeviceForPolling for device {DeviceId} with new host {Host}", id, newHost);
        }

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

    [HttpGet("{id:guid}/history/state")]
    public async Task<IActionResult> GetStateHistory(
        Guid id,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 20,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var entries = await _historyRepo.GetStateHistoryAsync(id, skip, limit, from, to);
        var response = entries.Select(e => new { e.Capability, e.Value, e.Timestamp });
        return Ok(response);
    }

    [HttpGet("{id:guid}/history/commands")]
    public async Task<IActionResult> GetCommandHistory(
        Guid id,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 20,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var entries = await _historyRepo.GetCommandHistoryAsync(id, skip, limit, from, to);
        var response = entries.Select(e => new { e.Capability, e.Value, e.Source, e.Timestamp });
        return Ok(response);
    }
}
