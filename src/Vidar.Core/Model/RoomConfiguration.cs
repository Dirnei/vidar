namespace Vidar.Core.Model;
public sealed class RoomConfiguration
{
    public static readonly Guid HomeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; init; }
    public required string Name { get; set; }
    public bool IsHome { get; init; }
}
