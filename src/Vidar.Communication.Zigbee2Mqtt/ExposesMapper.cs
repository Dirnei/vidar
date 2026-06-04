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
    };

    public static List<CapabilityType> MapCapabilities(JsonElement exposesArray)
    {
        var result = new HashSet<CapabilityType>();
        MapExposesArray(exposesArray, result);
        return [.. result];
    }

    private static void MapExposesArray(JsonElement array, HashSet<CapabilityType> result)
    {
        foreach (var item in array.EnumerateArray())
            MapExposesItem(item, result);
    }

    private static void MapExposesItem(JsonElement item, HashSet<CapabilityType> result)
    {
        var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

        switch (type)
        {
            case "light":
                result.Add(CapabilityType.Switch);
                result.Add(CapabilityType.Dimmer);
                if (item.TryGetProperty("features", out var lightFeatures))
                    MapExposesArray(lightFeatures, result);
                break;

            case "cover":
                result.Add(CapabilityType.Cover);
                if (item.TryGetProperty("features", out var coverFeatures))
                    MapExposesArray(coverFeatures, result);
                break;

            default:
                if (item.TryGetProperty("features", out var features))
                    MapExposesArray(features, result);

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
