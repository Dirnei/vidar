using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Model;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/rooms")]
public sealed class RoomsController : ControllerBase
{
    private readonly IRoomRepository _roomRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IDeviceStateRepository _stateRepo;
    private readonly IGroupRepository _groupRepo;

    public RoomsController(IRoomRepository roomRepo, IDeviceRepository deviceRepo, IDeviceStateRepository stateRepo, IGroupRepository groupRepo)
    {
        _roomRepo = roomRepo;
        _deviceRepo = deviceRepo;
        _stateRepo = stateRepo;
        _groupRepo = groupRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rooms = await _roomRepo.GetAllAsync();
        var response = new List<RoomResponse>();
        foreach (var room in rooms)
        {
            var devices = await _deviceRepo.GetByRoomIdAsync(room.Id);
            response.Add(new RoomResponse(room.Id, room.Name, devices.Count, room.IsHome));
        }
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoomRequest request)
    {
        var room = new RoomConfiguration { Id = Guid.NewGuid(), Name = request.Name };
        await _roomRepo.CreateAsync(room);
        var response = new RoomResponse(room.Id, room.Name, 0, room.IsHome);
        return CreatedAtAction(nameof(GetAll), new { id = room.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoomRequest request)
    {
        var room = await _roomRepo.GetByIdAsync(id);
        if (room == null) return NotFound();
        room.Name = request.Name;
        await _roomRepo.UpdateAsync(room);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var room = await _roomRepo.GetByIdAsync(id);
        if (room == null) return NotFound();
        await _roomRepo.DeleteAsync(id);
        return NoContent();
    }

    [HttpGet("{id:guid}/devices")]
    public async Task<IActionResult> GetDevices(Guid id)
    {
        var room = await _roomRepo.GetByIdAsync(id);
        if (room == null) return NotFound();
        var devices = await _deviceRepo.GetByRoomIdAsync(id);
        var groups = await _groupRepo.GetByRoomIdAsync(id);
        // Exclude devices that belong to a group in this room
        var groupedDeviceIds = groups.SelectMany(g => g.DeviceIds).ToHashSet();
        devices = devices.Where(d => !groupedDeviceIds.Contains(d.Id)).ToList();
        var states = await _stateRepo.GetAllAsync();
        var stateMap = states.ToDictionary(s => s.DeviceId);
        var response = devices.Select(d =>
        {
            stateMap.TryGetValue(d.Id, out var state);
            var stateDict = state?.States;
            return new DeviceResponse(d.Id, d.Name, d.RoomId, room.Name, d.CommunicationType, d.Capabilities, stateDict, state?.Online, d.Settings, null, null);
        }).ToList();
        return Ok(response);
    }

    [HttpGet("{id:guid}/groups")]
    public async Task<IActionResult> GetGroups(Guid id)
    {
        var room = await _roomRepo.GetByIdAsync(id);
        if (room == null) return NotFound();
        var groups = await _groupRepo.GetByRoomIdAsync(id);
        var allDevices = await _deviceRepo.GetAllAsync();
        var allStates = await _stateRepo.GetAllAsync();
        var deviceMap = allDevices.ToDictionary(d => d.Id);
        var stateMap = allStates.ToDictionary(s => s.DeviceId);
        var response = groups.Select(g => BuildGroupResponse(g, room.Name, deviceMap, stateMap)).ToList();
        return Ok(response);
    }

    private static GroupResponse BuildGroupResponse(
        Vidar.Core.Model.GroupConfiguration group,
        string? roomName,
        Dictionary<Guid, Vidar.Core.Model.DeviceConfiguration> deviceMap,
        Dictionary<Guid, Vidar.Core.Model.DeviceState> stateMap)
    {
        var members = group.DeviceIds
            .Select(id => deviceMap.TryGetValue(id, out var d) ? d : null)
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
