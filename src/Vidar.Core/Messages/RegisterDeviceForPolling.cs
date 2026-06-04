using Vidar.Core.Capabilities;
namespace Vidar.Core.Messages;
public sealed record RegisterDeviceForPolling(
    Guid DeviceId,
    string CommunicationType,
    string NativeId,
    string Host,
    int Generation,
    List<CapabilityType> Capabilities);
