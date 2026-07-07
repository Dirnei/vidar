namespace Vidar.Communication.Loxone;

// Child (LoxoneMiniserverActor) tells the bridge the parsed structure so the bridge can run
// Discover() for each control (Discover lives on PluginActorBase).
public sealed record ControlsDiscovered(string Serial, LoxoneStructure Structure);

// Bridge's reply to ControlsDiscovered (Sender is the child that sent it). The bridge already
// computes each control's stable deviceId via GetDeviceId/Discover; the child needs that map to
// tag DeviceStateUpdates and route commands without re-deriving id assignment itself.
public sealed record ControlIds(string Serial, Dictionary<string, Guid> ByUuid);
