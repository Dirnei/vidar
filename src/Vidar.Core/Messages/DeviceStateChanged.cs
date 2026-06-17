namespace Vidar.Core.Messages;
public sealed record DeviceStateChanged(Guid DeviceId, string CapabilityKey, object Value, DateTime Timestamp) : IWithDeviceId;
