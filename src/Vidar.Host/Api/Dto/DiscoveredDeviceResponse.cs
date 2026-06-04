using Vidar.Core.Capabilities;

namespace Vidar.Host.Api.Dto;

public sealed record DiscoveredDeviceResponse(
    Guid Id,
    string CommunicationType,
    string NativeId,
    List<CapabilityType> Capabilities,
    Dictionary<string, string> Metadata,
    DateTime DiscoveredAt);
