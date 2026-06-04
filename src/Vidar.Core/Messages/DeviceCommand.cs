using Vidar.Core.Capabilities;
namespace Vidar.Core.Messages;
public sealed record DeviceCommand(Guid DeviceId, string CommunicationType, string NativeId, CapabilityType Capability, object Value);
