namespace Vidar.Host.Api.Dto;

public sealed record RoomResponse(Guid Id, string Name, int DeviceCount, bool IsHome);
