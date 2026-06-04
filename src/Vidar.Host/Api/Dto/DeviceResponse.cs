using Vidar.Core.Capabilities;

namespace Vidar.Host.Api.Dto;

public sealed record DeviceResponse(
    Guid Id,
    string Name,
    Guid RoomId,
    string? RoomName,
    string CommunicationType,
    List<CapabilityType> Capabilities,
    Dictionary<string, object>? State);
