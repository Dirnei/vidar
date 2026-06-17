namespace Vidar.Core.Messages;
public sealed record DeviceCommand(Guid DeviceId, string CommunicationType, string NativeId, string CapabilityKey, object Value) : IWithDeviceId;
