using Vidar.Core.Capabilities;

namespace Vidar.Communication.Shelly;

public sealed class ShellyDevice
{
    public required string NativeId { get; init; }
    public required string Host { get; init; }
    public int Generation { get; init; } = 2;
    public List<CapabilityType> Capabilities { get; init; } = [];
    public Guid? VidarDeviceId { get; set; }
}
