using Vidar.Core.Capabilities;
namespace Vidar.Core.Model;
public sealed class DeviceConfiguration
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public Guid RoomId { get; set; }
    public required string CommunicationType { get; init; }
    public required string NativeId { get; init; }
    public required List<CapabilityDescriptor> Capabilities { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
}
