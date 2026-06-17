using System.Text.Json;
using TurboHomeConnect.Model;

namespace Vidar.Communication.HomeConnect;

public static class HomeConnectStateMapper
{
    public static List<(string CapabilityKey, object Value)> MapEventItems(IReadOnlyList<EventItem> items)
    {
        var result = new List<(string, object)>();

        foreach (var item in items)
        {
            var key = SimplifyKey(item.Key);
            var value = SimplifyValue(item.Value);
            result.Add((key, value));
        }

        return result;
    }

    public static string SimplifyKey(string fullKey)
    {
        var lastDot = fullKey.LastIndexOf('.');
        return lastDot >= 0 ? fullKey[(lastDot + 1)..] : fullKey;
    }

    public static string SimplifyEnumValue(string fullValue)
    {
        if (!fullValue.Contains("EnumType", StringComparison.Ordinal))
            return fullValue;

        var lastDot = fullValue.LastIndexOf('.');
        return lastDot >= 0 ? fullValue[(lastDot + 1)..] : fullValue;
    }

    private static object SimplifyValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString()!;
            return str.Contains("EnumType", StringComparison.Ordinal)
                ? SimplifyEnumValue(str)
                : (object)str;
        }

        return value;
    }
}
