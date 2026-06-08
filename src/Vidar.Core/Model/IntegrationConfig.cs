namespace Vidar.Core.Model;

public enum ApplicationType
{
    Provider,
    Consumer
}

public sealed class ApplicationConfig
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public ApplicationType ApplicationType { get; set; }
    public bool Enabled { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
}
