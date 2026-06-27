namespace Vidar.Host.Api.Dto;

public sealed record ConfigureDeviceRequest(string Name, Guid RoomId)
{
    public Dictionary<string, string>? Settings { get; set; }
}
