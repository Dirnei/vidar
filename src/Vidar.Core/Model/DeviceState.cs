using Vidar.Core.Capabilities;
namespace Vidar.Core.Model;
public sealed class DeviceState
{
    public Guid DeviceId { get; init; }
    public Dictionary<CapabilityType, object> States { get; init; } = new();
    public DateTime LastUpdated { get; set; }
}
