using Vidar.Core.Capabilities;
namespace Vidar.Core.Model;
public sealed class DiscoveredDevice
{
    public Guid Id { get; init; }
    public required string CommunicationType { get; init; }
    public required string NativeId { get; init; }
    public required List<CapabilityDescriptor> Capabilities { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public DateTime DiscoveredAt { get; init; }
}
