namespace Vidar.Core.Model;
public sealed class GroupConfiguration
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public Guid RoomId { get; set; }
    public required List<Guid> DeviceIds { get; init; }
}
