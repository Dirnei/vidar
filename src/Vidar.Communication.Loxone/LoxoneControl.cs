namespace Vidar.Communication.Loxone;

public sealed record LoxoneMood(int Id, string Name);

// A Loxone control as parsed from LoxAPP3.json (republished by the sidecar in the structure
// manifest). Present-fields: only the fields Phase A needs. Moods is empty except for
// LightControllerV2.
public sealed record LoxoneControl(
    string Uuid,
    string Name,
    string Type,
    string? RoomUuid,
    IReadOnlyList<LoxoneMood> Moods);
