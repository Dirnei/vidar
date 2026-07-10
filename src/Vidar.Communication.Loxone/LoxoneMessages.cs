namespace Vidar.Communication.Loxone;

// Child (LoxoneMiniserverActor) tells the bridge the parsed structure so the bridge can run
// Discover() for each control (Discover lives on PluginActorBase).
public sealed record ControlsDiscovered(string Serial, LoxoneStructure Structure);

// Bridge's reply to ControlsDiscovered (Sender is the child that sent it). The bridge already
// computes each control's stable deviceId via GetDeviceId/Discover; the child needs that map to
// tag DeviceStateUpdates and route commands without re-deriving id assignment itself.
public sealed record ControlIds(string Serial, Dictionary<string, Guid> ByUuid);

// Bridge -> child: a device registration resolved (or changed) a control's stable deviceId after
// the child already ran discovery. Retained MQTT structure can race the cluster registration
// round-trip, so a control may have been tagged with an unresolved (ephemeral) id; this re-tags it
// and flushes the control's last cached state to the now-correct twin.
public sealed record ResolveControlId(string Uuid, Guid DeviceId);
