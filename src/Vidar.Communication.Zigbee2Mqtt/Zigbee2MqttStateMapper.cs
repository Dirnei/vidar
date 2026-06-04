using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt;

public record Zigbee2MqttCapabilityValue(CapabilityType Capability, object Value);

public static class Zigbee2MqttStateMapper
{
    private static readonly Dictionary<string, CapabilityType> NameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["state"] = CapabilityType.Switch,
        ["brightness"] = CapabilityType.Dimmer,
        ["position"] = CapabilityType.Cover,
        ["temperature"] = CapabilityType.Temperature,
        ["occupancy"] = CapabilityType.Motion,
        ["power"] = CapabilityType.Power,
        ["energy"] = CapabilityType.Energy,
        ["humidity"] = CapabilityType.Humidity,
    };

    public static List<Zigbee2MqttCapabilityValue> MapState(string json, IReadOnlyList<CapabilityType> knownCapabilities)
    {
        var result = new List<Zigbee2MqttCapabilityValue>();
        var knownSet = new HashSet<CapabilityType>(knownCapabilities);

        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!NameMap.TryGetValue(prop.Name, out var cap)) continue;
            if (!knownSet.Contains(cap)) continue;

            object? value = cap switch
            {
                CapabilityType.Switch => MapSwitchValue(prop.Value),
                CapabilityType.Dimmer => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                CapabilityType.Cover => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : null,
                CapabilityType.Temperature => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                CapabilityType.Motion => prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False
                    ? prop.Value.GetBoolean()
                    : null,
                CapabilityType.Power => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                CapabilityType.Energy => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                CapabilityType.Humidity => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                _ => null
            };

            if (value != null)
                result.Add(new Zigbee2MqttCapabilityValue(cap, value));
        }

        return result;
    }

    private static object? MapSwitchValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
            return element.GetBoolean();
        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (string.Equals(str, "ON", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(str, "OFF", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return null;
    }
}
