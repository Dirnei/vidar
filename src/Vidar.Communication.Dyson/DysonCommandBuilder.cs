using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vidar.Communication.Dyson;

public static class DysonCommandBuilder
{
    private sealed record StateSet(
        [property: JsonPropertyName("msg")] string Msg,
        [property: JsonPropertyName("time")] string Time,
        [property: JsonPropertyName("mode-reason")] string ModeReason,
        [property: JsonPropertyName("data")] Dictionary<string, string> Data);

    public static string? Build(string capabilityKey, object value, DateTimeOffset now)
    {
        var data = capabilityKey switch
        {
            "power" => Field("fpwr", OnOff(value)),
            "auto" => Field("auto", OnOff(value)),
            "night_mode" => Field("nmod", OnOff(value)),
            "oscillation" => Field("oson", OnOff(value)),
            "continuous_monitoring" => Field("rhtm", OnOff(value)),
            "heat" => Field("hmod", value is true ? "HEAT" : "OFF"),
            "humidify" => Field("hume", value is true ? "HUMD" : "OFF"),
            "auto_humidify" => Field("haut", OnOff(value)),
            "fan_speed" => Field("fnsp", Padded(value, 1, 10)),
            "target_humidity" => Field("humt", Padded(value, 30, 70)),
            "target_temperature" => Field("hmax", Kelvin(value)),
            _ => null,
        };
        if (data is null) return null;

        return JsonSerializer.Serialize(new StateSet(
            "STATE-SET",
            now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            "LAPP",
            data));
    }

    private static Dictionary<string, string> Field(string k, string v) => new() { [k] = v };

    private static string OnOff(object v) => v switch
    {
        bool b => b ? "ON" : "OFF",
        string s when bool.TryParse(s, out var b) => b ? "ON" : "OFF",
        _ => "OFF",
    };

    private static int ToInt(object v) => v switch
    {
        double d => (int)Math.Round(d),
        int i => i,
        string s when int.TryParse(s, out var p) => p,
        _ => 0,
    };

    private static string Padded(object v, int min, int max) =>
        Math.Clamp(ToInt(v), min, max).ToString("D4");

    // Heat target: Dyson expects Kelvin tenths as a 4-digit string.
    private static string Kelvin(object v) =>
        ((int)Math.Round((ToInt(v) + 273.15) * 10)).ToString("D4");
}
