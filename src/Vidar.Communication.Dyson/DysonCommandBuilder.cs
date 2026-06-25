using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vidar.Communication.Dyson;

public static class DysonCommandBuilder
{
    public static string? Build(string capabilityKey, object value, DateTimeOffset now)
    {
        var data = capabilityKey switch
        {
            "power"                 => Field("fpwr", OnOff(value)),
            "auto"                  => Field("auto", OnOff(value)),
            "night_mode"            => Field("nmod", OnOff(value)),
            "oscillation"           => Field("oson", OnOff(value)),
            "continuous_monitoring" => Field("rhtm", OnOff(value)),
            "fan_speed"             => Field("fnsp", FanSpeed(value)),
            _                       => null,
        };

        if (data is null) return null;

        var envelope = new StateSetEnvelope(
            Msg: "STATE-SET",
            Time: now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ModeReason: "LAPP",
            Data: data);

        return JsonSerializer.Serialize(envelope);
    }

    private static Dictionary<string, string> Field(string key, string value)
        => new() { [key] = value };

    private static string OnOff(object value) => value switch
    {
        bool b                                        => b ? "ON" : "OFF",
        string s when bool.TryParse(s, out var b)     => b ? "ON" : "OFF",
        _                                             => "OFF",
    };

    private static string FanSpeed(object value)
    {
        var n = value switch
        {
            double d                                      => (int)d,
            int i                                         => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _                                             => 1,
        };
        n = Math.Clamp(n, 1, 10);
        return n.ToString("D4");
    }

    private record StateSetEnvelope(
        [property: JsonPropertyName("msg")]         string Msg,
        [property: JsonPropertyName("time")]        string Time,
        [property: JsonPropertyName("mode-reason")] string ModeReason,
        [property: JsonPropertyName("data")]        Dictionary<string, string> Data);
}
