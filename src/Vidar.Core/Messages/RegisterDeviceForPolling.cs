using Vidar.Core.Capabilities;
namespace Vidar.Core.Messages;
public sealed record RegisterDeviceForPolling(
    Guid DeviceId,
    string CommunicationType,
    string NativeId,
    string Host,
    int Generation,
    List<CapabilityDescriptor> Capabilities,
    // Per-device settings (persisted on the DeviceConfiguration) carried through to the worker.
    // Lets a plugin recover a device's connection secrets (e.g. a Bambu printer's access code)
    // on restart. Null for plugins/paths that don't need it (e.g. Shelly, which uses Host).
    Dictionary<string, string>? Settings = null);
