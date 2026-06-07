using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Model;
using Vidar.Host.Api;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Api;

public class RoomsControllerTests
{
    private readonly IRoomRepository _roomRepo = Substitute.For<IRoomRepository>();
    private readonly IDeviceRepository _deviceRepo = Substitute.For<IDeviceRepository>();
    private readonly RoomsController _sut;

    public RoomsControllerTests()
    {
        _sut = new RoomsController(_roomRepo, _deviceRepo, Substitute.For<IDeviceStateRepository>(), Substitute.For<IGroupRepository>());
    }

    [Fact]
    public async Task GetAll_ReturnsRoomsWithDeviceCounts()
    {
        var roomId = Guid.NewGuid();
        _roomRepo.GetAllAsync().Returns([new RoomConfiguration { Id = roomId, Name = "Kitchen" }]);
        _deviceRepo.GetByRoomIdAsync(roomId).Returns([
            new DeviceConfiguration
            {
                Id = Guid.NewGuid(), Name = "Plug", CommunicationType = "shelly",
                NativeId = "s1", Capabilities = [Vidar.Core.Capabilities.CapabilityType.Switch]
            }
        ]);

        var result = await _sut.GetAll();
        var ok = Assert.IsType<OkObjectResult>(result);
        var rooms = Assert.IsAssignableFrom<List<RoomResponse>>(ok.Value);
        Assert.Single(rooms);
        Assert.Equal("Kitchen", rooms[0].Name);
        Assert.Equal(1, rooms[0].DeviceCount);
    }

    [Fact]
    public async Task Create_ReturnsCreatedRoom()
    {
        var result = await _sut.Create(new CreateRoomRequest("Living Room"));
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var room = Assert.IsType<RoomResponse>(created.Value);
        Assert.Equal("Living Room", room.Name);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        _roomRepo.GetByIdAsync(Arg.Any<Guid>()).Returns((RoomConfiguration?)null);
        var result = await _sut.Delete(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }
}
