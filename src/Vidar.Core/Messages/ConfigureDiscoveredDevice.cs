namespace Vidar.Core.Messages;
public sealed record ConfigureDiscoveredDevice(Guid DiscoveredDeviceId, string Name, Guid RoomId);
