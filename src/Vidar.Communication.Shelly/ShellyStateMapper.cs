using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Shelly;

public record ShellyCapabilityValue(CapabilityType Capability, object Value);

public static class ShellyStateMapper
{
    public static List<ShellyCapabilityValue> MapSwitchStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();

        if (element.TryGetProperty("output", out var output) && (output.ValueKind == JsonValueKind.True || output.ValueKind == JsonValueKind.False))
            results.Add(new ShellyCapabilityValue(CapabilityType.Switch, output.GetBoolean()));

        if (element.TryGetProperty("apower", out var apower) && apower.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue(CapabilityType.Power, apower.GetDouble()));

        if (element.TryGetProperty("aenergy", out var aenergy) && aenergy.ValueKind == JsonValueKind.Object)
        {
            if (aenergy.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
                results.Add(new ShellyCapabilityValue(CapabilityType.Energy, total.GetDouble()));
        }

        return results;
    }

    public static List<ShellyCapabilityValue> MapCoverStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();

        if (element.TryGetProperty("current_pos", out var pos) && pos.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue(CapabilityType.Cover, pos.GetInt32()));

        return results;
    }

    public static List<ShellyCapabilityValue> MapTemperatureStatus(JsonElement element)
    {
        var results = new List<ShellyCapabilityValue>();

        if (element.TryGetProperty("tC", out var tC) && tC.ValueKind == JsonValueKind.Number)
            results.Add(new ShellyCapabilityValue(CapabilityType.Temperature, tC.GetDouble()));

        return results;
    }
}
