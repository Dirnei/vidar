using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt;

public record Zigbee2MqttCapabilityValue(string CapabilityKey, object Value);

public static class Zigbee2MqttStateMapper
{
    private static readonly Dictionary<string, string> NameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["state"] = "switch",
        ["brightness"] = "dimmer",
        ["position"] = "cover",
        ["temperature"] = "temperature",
        ["occupancy"] = "motion",
        ["power"] = "power",
        ["energy"] = "energy",
        ["humidity"] = "humidity",
        ["contact"] = "contact",
        ["action"] = "action",
        ["battery"] = "battery",
    };

    public static List<Zigbee2MqttCapabilityValue> MapState(string json, IReadOnlyList<CapabilityDescriptor> knownCapabilities)
    {
        var result = new List<Zigbee2MqttCapabilityValue>();
        var knownKeys = new HashSet<string>(knownCapabilities.Select(c => c.Key));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Light is composite
        if (knownKeys.Contains("light"))
        {
            var lightState = new Dictionary<string, object>();

            if (root.TryGetProperty("state", out var stateProp))
            {
                var on = MapSwitchValue(stateProp) as bool?;
                if (on.HasValue) lightState["on"] = on.Value;
            }
            if (root.TryGetProperty("brightness", out var brProp) && brProp.ValueKind == JsonValueKind.Number)
                lightState["brightness"] = Math.Round(brProp.GetDouble() / 254.0 * 100.0);
            if (root.TryGetProperty("color_temp", out var ctProp) && ctProp.ValueKind == JsonValueKind.Number)
                lightState["color_temp"] = ctProp.GetInt32();
            if (root.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.Object)
            {
                if (colorProp.TryGetProperty("x", out var cx) && colorProp.TryGetProperty("y", out var cy))
                {
                    lightState["color_x"] = cx.GetDouble();
                    lightState["color_y"] = cy.GetDouble();
                }
                if (colorProp.TryGetProperty("hue", out var hue) && colorProp.TryGetProperty("saturation", out var sat))
                {
                    lightState["color_h"] = hue.GetDouble();
                    lightState["color_s"] = sat.GetDouble();
                }
            }
            if (root.TryGetProperty("color_mode", out var modeProp) && modeProp.ValueKind == JsonValueKind.String)
                lightState["color_mode"] = modeProp.GetString()!;

            if (lightState.Count > 0)
                result.Add(new Zigbee2MqttCapabilityValue("light", lightState));
        }

        // Update is composite
        if (root.TryGetProperty("update", out var updateProp) &&
            updateProp.ValueKind == JsonValueKind.Object)
        {
            var updateState = new Dictionary<string, object>();
            if (updateProp.TryGetProperty("state", out var uState) && uState.ValueKind == JsonValueKind.String)
                updateState["state"] = uState.GetString()!;
            if (updateProp.TryGetProperty("installed_version", out var iv))
                updateState["installed_version"] = iv.ValueKind == JsonValueKind.Number ? iv.GetInt64() : (object)(iv.GetString() ?? "");
            if (updateProp.TryGetProperty("latest_version", out var lv))
                updateState["latest_version"] = lv.ValueKind == JsonValueKind.Number ? lv.GetInt64() : (object)(lv.GetString() ?? "");
            if (updateProp.TryGetProperty("progress", out var prog) && prog.ValueKind == JsonValueKind.Number)
                updateState["progress"] = prog.GetDouble();
            if (updateState.Count > 0)
                result.Add(new Zigbee2MqttCapabilityValue("update", updateState));
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "update") continue;

            if (NameMap.TryGetValue(prop.Name, out var capKey) && knownKeys.Contains(capKey))
            {
                // Skip switch/dimmer when light is present (they're part of the composite)
                if (knownKeys.Contains("light") && (capKey == "switch" || capKey == "dimmer"))
                    continue;

                object? value = capKey switch
                {
                    "switch" => MapSwitchValue(prop.Value),
                    "dimmer" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    "cover" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : null,
                    "temperature" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    "motion" => prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? prop.Value.GetBoolean() : null,
                    "power" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    "energy" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    "humidity" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    "contact" => prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? prop.Value.GetBoolean() : null,
                    "action" => prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null,
                    "battery" => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    _ => null
                };

                if (value != null)
                    result.Add(new Zigbee2MqttCapabilityValue(capKey, value));
            }
        }

        return result;
    }

    private static object? MapSwitchValue(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
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
