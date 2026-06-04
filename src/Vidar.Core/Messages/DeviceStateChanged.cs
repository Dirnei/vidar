using Vidar.Core.Capabilities;
namespace Vidar.Core.Messages;
public sealed record DeviceStateChanged(Guid DeviceId, CapabilityType Capability, object Value, DateTime Timestamp);
