using Vidar.Core.Capabilities;

namespace Vidar.Communication.Shelly;

public sealed class ShellyDevice
{
    public required string NativeId { get; init; }
    public required string Host { get; init; }
    public int Generation { get; init; } = 2;

    /// <summary>
    /// Component channel on the physical device (e.g. cover:0 vs cover:1). Multi-channel devices
    /// are modelled as one Vidar device per channel, addressed by a "MAC-&lt;channel&gt;" NativeId.
    /// </summary>
    public int Channel { get; init; }

    public List<CapabilityDescriptor> Capabilities { get; init; } = [];
    public Guid? VidarDeviceId { get; set; }
}
