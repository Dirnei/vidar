using Vidar.Core.Capabilities;
namespace Vidar.Core.Messages;
public sealed record DeviceDiscovered(Guid DeviceId, string CommunicationType, string NativeId, IReadOnlyList<CapabilityDescriptor> Capabilities, Dictionary<string, string> Metadata);
