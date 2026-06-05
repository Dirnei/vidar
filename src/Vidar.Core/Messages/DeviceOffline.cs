namespace Vidar.Core.Messages;
public sealed record DeviceOffline(Guid DeviceId) : IWithDeviceId;
