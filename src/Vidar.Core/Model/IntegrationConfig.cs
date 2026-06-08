namespace Vidar.Core.Model;

public sealed class IntegrationConfig
{
    public required string Id { get; init; }  // e.g. "unifi", "zigbee2mqtt", "shelly"
    public required string Type { get; init; }
    public bool Enabled { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
}
