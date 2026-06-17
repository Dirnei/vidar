using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt;

public static class ExposesMapper
{
    private static readonly Dictionary<string, Func<CapabilityDescriptor>> DescriptorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["state"] = () => new() { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true },
        ["brightness"] = () => new() { Key = "dimmer", Label = "Dimmer", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 254 },
        ["position"] = () => new() { Key = "cover", Label = "Cover", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 },
        ["temperature"] = () => new() { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius },
        ["occupancy"] = () => new() { Key = "motion", Label = "Motion", Unit = UnitType.Detected },
        ["power"] = () => new() { Key = "power", Label = "Power", Unit = UnitType.Watts },
        ["energy"] = () => new() { Key = "energy", Label = "Energy", Unit = UnitType.WattHours },
        ["humidity"] = () => new() { Key = "humidity", Label = "Humidity", Unit = UnitType.Percent },
        ["contact"] = () => new() { Key = "contact", Label = "Contact", Unit = UnitType.OpenClosed },
        ["action"] = () => new() { Key = "action", Label = "Action", Unit = UnitType.Text },
        ["battery"] = () => new() { Key = "battery", Label = "Battery", Unit = UnitType.Percent, Min = 0, Max = 100 },
        ["illuminance_lux"] = () => new() { Key = "illuminance", Label = "Illuminance", Unit = UnitType.Lux },
    };

    public static List<CapabilityDescriptor> MapCapabilities(JsonElement exposesArray)
    {
        var result = new Dictionary<string, CapabilityDescriptor>();
        var lightFeatureNames = new HashSet<string>();
        var actionValues = new List<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames, actionValues);
        return [.. result.Values];
    }

    public static HashSet<string> ExtractLightFeatures(JsonElement exposesArray)
    {
        var result = new Dictionary<string, CapabilityDescriptor>();
        var lightFeatureNames = new HashSet<string>();
        var actionValues = new List<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames, actionValues);
        return lightFeatureNames;
    }

    public static List<string> ExtractActionValues(JsonElement exposesArray)
    {
        var result = new Dictionary<string, CapabilityDescriptor>();
        var lightFeatureNames = new HashSet<string>();
        var actionValues = new List<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames, actionValues);
        return actionValues;
    }

    private static void MapExposesArray(JsonElement array, Dictionary<string, CapabilityDescriptor> result, HashSet<string> lightFeatures, List<string> actionValues)
    {
        foreach (var item in array.EnumerateArray())
            MapExposesItem(item, result, lightFeatures, actionValues);
    }

    private static void MapExposesItem(JsonElement item, Dictionary<string, CapabilityDescriptor> result, HashSet<string> lightFeatures, List<string> actionValues)
    {
        var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        switch (type)
        {
            case "light":
                result.TryAdd("light", new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true });
                if (item.TryGetProperty("features", out var lf))
                {
                    foreach (var feature in lf.EnumerateArray())
                    {
                        if (feature.TryGetProperty("name", out var fn))
                            lightFeatures.Add(fn.GetString() ?? "");
                    }
                }
                break;

            case "cover":
                result.TryAdd("cover", new CapabilityDescriptor { Key = "cover", Label = "Cover", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 });
                if (item.TryGetProperty("features", out var coverFeatures))
                    MapExposesArray(coverFeatures, result, lightFeatures, actionValues);
                break;

            case "update":
                result.TryAdd("update", new CapabilityDescriptor { Key = "update", Label = "Update", Unit = UnitType.Text });
                break;

            default:
                if (item.TryGetProperty("features", out var features))
                    MapExposesArray(features, result, lightFeatures, actionValues);

                if (item.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (name != null && DescriptorMap.TryGetValue(name, out var factory))
                    {
                        var descriptor = factory();
                        result.TryAdd(descriptor.Key, descriptor);
                        if (descriptor.Key == "action" &&
                            item.TryGetProperty("values", out var vals) &&
                            vals.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var v in vals.EnumerateArray())
                            {
                                var s = v.GetString();
                                if (s != null) actionValues.Add(s);
                            }
                        }
                    }
                }
                break;
        }
    }
}
