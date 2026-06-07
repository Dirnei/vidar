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
        MapExposesArray(exposesArray, result, lightFeatureNames);
        return [.. result];
    }

    public static HashSet<string> ExtractLightFeatures(JsonElement exposesArray)
    {
        var result = new HashSet<CapabilityType>();
        var lightFeatureNames = new HashSet<string>();
        MapExposesArray(exposesArray, result, lightFeatureNames);
        return lightFeatureNames;
    }

    private static void MapExposesArray(JsonElement array, HashSet<CapabilityType> result, HashSet<string> lightFeatures)
    {
        foreach (var item in array.EnumerateArray())
            MapExposesItem(item, result, lightFeatures);
    }

    private static void MapExposesItem(JsonElement item, HashSet<CapabilityType> result, HashSet<string> lightFeatures)
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
                    MapExposesArray(coverFeatures, result, lightFeatures);
                break;

            default:
                if (item.TryGetProperty("features", out var features))
                    MapExposesArray(features, result, lightFeatures);

                if (item.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (name != null && NameMap.TryGetValue(name, out var cap))
                        result.Add(cap);
                }
                break;
        }
    }
}
