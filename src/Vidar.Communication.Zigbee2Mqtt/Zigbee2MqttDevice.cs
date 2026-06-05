using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt;

public sealed class Zigbee2MqttDevice
{
    public required string IeeeAddress { get; init; }
    public required string FriendlyName { get; set; }
    public List<CapabilityType> Capabilities { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new();
    public Guid? VidarDeviceId { get; set; }
}
