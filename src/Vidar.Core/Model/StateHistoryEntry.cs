namespace Vidar.Core.Model;
public sealed class StateHistoryEntry
{
    public Guid DeviceId { get; init; }
    public required string Capability { get; init; }
    public required object Value { get; init; }
    public DateTime Timestamp { get; init; }
}
