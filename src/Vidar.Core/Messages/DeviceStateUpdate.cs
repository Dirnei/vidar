namespace Vidar.Core.Messages;
public sealed record DeviceStateUpdate(Guid DeviceId, string CapabilityKey, object Value) : IWithDeviceId;
