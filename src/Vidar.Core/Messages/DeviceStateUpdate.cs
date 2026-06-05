using Vidar.Core.Capabilities;
namespace Vidar.Core.Messages;
public sealed record DeviceStateUpdate(Guid DeviceId, CapabilityType Capability, object Value) : IWithDeviceId;
