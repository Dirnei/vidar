namespace Vidar.Host.Api.Dto;

public sealed record CreateGroupRequest(string Name, Guid RoomId, List<Guid> DeviceIds);
