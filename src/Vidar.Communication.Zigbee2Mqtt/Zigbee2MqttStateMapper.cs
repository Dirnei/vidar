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
        ["contact"] = CapabilityType.Contact,
        ["action"] = CapabilityType.Action,
        ["battery"] = CapabilityType.Battery,
    };

    public static List<Zigbee2MqttCapabilityValue> MapState(string json, IReadOnlyList<CapabilityType> knownCapabilities)
    {
        var result = new List<Zigbee2MqttCapabilityValue>();
        var knownSet = new HashSet<CapabilityType>(knownCapabilities);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Light is composite: combines state + brightness + color into one value
        if (knownSet.Contains(CapabilityType.Light))
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
                result.Add(new Zigbee2MqttCapabilityValue(CapabilityType.Light, lightState));
        }

        // Update is composite like Light; always parse if present (OTA support
        // may not appear in exposes but does appear in device state)
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
                result.Add(new Zigbee2MqttCapabilityValue(CapabilityType.Update, updateState));
        }

        var extras = new Dictionary<string, object>();

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "update")
                continue;

            if (NameMap.TryGetValue(prop.Name, out var cap) && knownSet.Contains(cap))
            {
                if (knownSet.Contains(CapabilityType.Light) && (cap == CapabilityType.Switch || cap == CapabilityType.Dimmer))
                    continue;

                object? value = cap switch
                {
                    CapabilityType.Switch => MapSwitchValue(prop.Value),
                    CapabilityType.Dimmer => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    CapabilityType.Cover => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32() : null,
                    CapabilityType.Temperature => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    CapabilityType.Motion => prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False
                        ? prop.Value.GetBoolean() : null,
                    CapabilityType.Power => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    CapabilityType.Energy => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    CapabilityType.Humidity => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    CapabilityType.Contact => prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False
                        ? prop.Value.GetBoolean() : null,
                    CapabilityType.Action => prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null,
                    CapabilityType.Battery => prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetDouble() : null,
                    _ => null
                };

                if (value != null)
                    result.Add(new Zigbee2MqttCapabilityValue(cap, value));
                continue;
            }

            // Capture unmapped properties as extras
            extras[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.String => prop.Value.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => prop.Value.GetRawText(),
                JsonValueKind.Array => prop.Value.GetRawText(),
                _ => prop.Value.GetRawText()
            };
        }

        if (extras.Count > 0)
            result.Add(new Zigbee2MqttCapabilityValue(CapabilityType.Extras, extras));

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
