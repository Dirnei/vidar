using System.Text.Json;
using Vidar.Core.Capabilities;

namespace Vidar.Communication.Zigbee2Mqtt;

public static class ExposesMapper
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

    public static List<CapabilityType> MapCapabilities(JsonElement exposesArray)
    {
        var result = new HashSet<CapabilityType>();
        var lightFeatureNames = new HashSet<string>();
        var actionValues = new List<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames, actionValues);
        return [.. result];
    }

    public static HashSet<string> ExtractLightFeatures(JsonElement exposesArray)
    {
        var result = new HashSet<CapabilityType>();
        var lightFeatureNames = new HashSet<string>();
        var actionValues = new List<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames, actionValues);
        return lightFeatureNames;
    }

    public static List<string> ExtractActionValues(JsonElement exposesArray)
    {
        var result = new HashSet<CapabilityType>();
        var lightFeatureNames = new HashSet<string>();
        var actionValues = new List<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames, actionValues);
        return actionValues;
    }

    private static void MapExposesArray(JsonElement array, HashSet<CapabilityType> result, HashSet<string> lightFeatures, List<string> actionValues)
    {
        foreach (var item in array.EnumerateArray())
            MapExposesItem(item, result, lightFeatures, actionValues);
    }

    private static void MapExposesItem(JsonElement item, HashSet<CapabilityType> result, HashSet<string> lightFeatures, List<string> actionValues)
    {
        var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        switch (type)
        {
            case "light":
                result.Add(CapabilityType.Light);
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
                result.Add(CapabilityType.Cover);
                if (item.TryGetProperty("features", out var coverFeatures))
                    MapExposesArray(coverFeatures, result, lightFeatures, actionValues);
                break;

            case "update":
                result.Add(CapabilityType.Update);
                break;

            default:
                if (item.TryGetProperty("features", out var features))
                    MapExposesArray(features, result, lightFeatures, actionValues);

                if (item.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (name != null && NameMap.TryGetValue(name, out var cap))
                    {
                        result.Add(cap);
                        if (cap == CapabilityType.Action &&
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
