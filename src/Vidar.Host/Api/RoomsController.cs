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

    public RoomsController(IRoomRepository roomRepo, IDeviceRepository deviceRepo)
    {
        _roomRepo = roomRepo;
        _deviceRepo = deviceRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rooms = await _roomRepo.GetAllAsync();
        var response = new List<RoomResponse>();
        foreach (var room in rooms)
        {
            var devices = await _deviceRepo.GetByRoomIdAsync(room.Id);
            response.Add(new RoomResponse(room.Id, room.Name, devices.Count));
        }
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoomRequest request)
    {
        var room = new RoomConfiguration { Id = Guid.NewGuid(), Name = request.Name };
        await _roomRepo.CreateAsync(room);
        var response = new RoomResponse(room.Id, room.Name, 0);
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
        var response = devices.Select(d => new DeviceResponse(
            d.Id, d.Name, d.RoomId, room.Name, d.CommunicationType, d.Capabilities, null)).ToList();
        return Ok(response);
    }
}
