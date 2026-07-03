using System.Text.Json;

namespace Vidar.Communication.Roborock;

public static class RoborockCommandBuilder
{
    private static readonly HashSet<string> Known = new()
    {
        "vacuum.start", "vacuum.stop", "vacuum.pause", "vacuum.dock",
        "vacuum.locate", "vacuum.fanPower", "vacuum.cleanSegments", "vacuum.runScene",
    };

    public static string? Build(string capabilityKey, object value)
    {
        if (!Known.Contains(capabilityKey)) return null;
        return JsonSerializer.Serialize(new { capability = capabilityKey, value });
    }
}
