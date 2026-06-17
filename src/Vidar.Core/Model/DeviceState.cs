namespace Vidar.Core.Model;
public sealed class DeviceState
{
    public Guid DeviceId { get; init; }
    public Dictionary<string, object> States { get; init; } = new();
    public DateTime LastUpdated { get; set; }
    public bool Online { get; set; } = true;
}
