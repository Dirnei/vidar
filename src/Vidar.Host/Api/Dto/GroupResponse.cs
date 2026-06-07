namespace Vidar.Host.Api.Dto;

public sealed record GroupResponse(Guid Id, string Name, Guid RoomId, string? RoomName, List<Guid> DeviceIds, List<string> Capabilities, Dictionary<string, object>? State, bool? Online);
