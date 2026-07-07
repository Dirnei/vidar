namespace Vidar.Core.Model;

// A mapping from an external system's room (e.g. a Loxone Miniserver room uuid) to a Vidar room.
// Generic — keyed by plugin + serial + external room id — so any integration that knows its rooms
// can reuse it. VidarRoomId null means "discovered but not yet mapped".
public sealed class RoomMapping
{
    public Guid Id { get; init; }
    public required string PluginId { get; set; }
    public required string Serial { get; set; }
    public required string ExternalRoomId { get; set; }
    public required string ExternalRoomName { get; set; }
    public Guid? VidarRoomId { get; set; }
}
