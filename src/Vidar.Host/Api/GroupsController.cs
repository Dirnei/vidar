using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Sharding;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/groups")]
public sealed class GroupsController : ControllerBase
{
    private readonly IGroupRepository _groupRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IDeviceStateRepository _stateRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly IRequiredActor<DeviceTwinRegion> _twinRegion;
    private readonly ILogger<GroupsController> _logger;

    public GroupsController(
        IGroupRepository groupRepo,
        IDeviceRepository deviceRepo,
        IDeviceStateRepository stateRepo,
        IRoomRepository roomRepo,
        IRequiredActor<DeviceTwinRegion> twinRegion,
        ILogger<GroupsController> logger)
    {
        _groupRepo = groupRepo;
        _deviceRepo = deviceRepo;
        _stateRepo = stateRepo;
        _roomRepo = roomRepo;
        _twinRegion = twinRegion;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var groups = await _groupRepo.GetAllAsync();
        var allDevices = await _deviceRepo.GetAllAsync();
        var allStates = await _stateRepo.GetAllAsync();
        var allRooms = await _roomRepo.GetAllAsync();
        var deviceMap = allDevices.ToDictionary(d => d.Id);
        var stateMap = allStates.ToDictionary(s => s.DeviceId);
        var roomMap = allRooms.ToDictionary(r => r.Id, r => r.Name);

        var response = groups.Select(g =>
        {
            roomMap.TryGetValue(g.RoomId, out var roomName);
            return BuildGroupResponse(g, roomName, deviceMap, stateMap);
        }).ToList();

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var group = await _groupRepo.GetByIdAsync(id);
        if (group == null) return NotFound();

        var allDevices = await _deviceRepo.GetAllAsync();
        var allStates = await _stateRepo.GetAllAsync();
        var room = await _roomRepo.GetByIdAsync(group.RoomId);
        var deviceMap = allDevices.ToDictionary(d => d.Id);
        var stateMap = allStates.ToDictionary(s => s.DeviceId);

        return Ok(BuildGroupResponse(group, room?.Name, deviceMap, stateMap));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest request)
    {
        var group = new GroupConfiguration
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            RoomId = request.RoomId,
            DeviceIds = request.DeviceIds
        };
        await _groupRepo.CreateAsync(group);
        return CreatedAtAction(nameof(GetById), new { id = group.Id }, new GroupResponse(
            group.Id, group.Name, group.RoomId, null, group.DeviceIds, [], null, null));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateGroupRequest request)
    {
        var group = await _groupRepo.GetByIdAsync(id);
        if (group == null) return NotFound();

        group.Name = request.Name;
        group.RoomId = request.RoomId;
        group.DeviceIds.Clear();
        group.DeviceIds.AddRange(request.DeviceIds);
        await _groupRepo.UpdateAsync(group);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await _groupRepo.GetByIdAsync(id);
        if (group == null) return NotFound();
        await _groupRepo.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/command")]
    public async Task<IActionResult> SendCommand(Guid id, [FromBody] DeviceCommandRequest request)
    {
        var group = await _groupRepo.GetByIdAsync(id);
        if (group == null) return NotFound();

        var value = UnwrapJsonElement(request.Value) ?? request.Value;
        var region = _twinRegion.ActorRef;

        foreach (var deviceId in group.DeviceIds)
        {
            var device = await _deviceRepo.GetByIdAsync(deviceId);
            if (device == null) continue;
            var command = new DeviceCommand(deviceId, device.CommunicationType, device.NativeId, request.CapabilityKey, value);
            _logger.LogInformation("Sending command {Capability}={Value} to device {DeviceId} (group {GroupId})",
                request.CapabilityKey, value, deviceId, id);
            region.Tell(command);
        }

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

    private static GroupResponse BuildGroupResponse(
        GroupConfiguration group,
        string? roomName,
        Dictionary<Guid, DeviceConfiguration> deviceMap,
        Dictionary<Guid, DeviceState> stateMap)
    {
        var members = group.DeviceIds
            .Select(did => deviceMap.TryGetValue(did, out var d) ? d : null)
            .Where(d => d != null)
            .Select(d => d!)
            .ToList();

        // Capabilities = intersection of all member capabilities
        List<string> capabilities;
        if (members.Count == 0)
        {
            capabilities = [];
        }
        else
        {
            var capSets = members.Select(d => d.Capabilities.Select(c => c.Key).ToHashSet()).ToList();
            var intersection = new HashSet<string>(capSets[0]);
            for (var i = 1; i < capSets.Count; i++)
                intersection.IntersectWith(capSets[i]);
            capabilities = [.. intersection];
        }

        // Leader = first member with a state, fallback to first member
        var leader = members.FirstOrDefault(d => stateMap.ContainsKey(d.Id)) ?? members.FirstOrDefault();
        Dictionary<string, object>? state = null;
        bool? online = null;
        if (leader != null)
        {
            stateMap.TryGetValue(leader.Id, out var leaderState);
            online = leaderState?.Online;
            if (leaderState != null)
            {
                var capSet = new HashSet<string>(capabilities);
                state = leaderState.States
                    .Where(kvp => capSet.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }

        return new GroupResponse(group.Id, group.Name, group.RoomId, roomName, group.DeviceIds, capabilities, state, online);
    }
}
