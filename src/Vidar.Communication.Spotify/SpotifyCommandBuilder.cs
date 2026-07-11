using System.Net.Http;
using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Maps a standardized capability command to a Spotify Web API request spec.
// Present-capabilities only: unknown keys return null (ignored by the actor).
public static class SpotifyCommandBuilder
{
    private static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>();

    // Device-targeted overload: maps a capability command for a specific speaker (deviceId) to a
    // Spotify Web API request. "Play here" (transfer) when the target is not the active device.
    public static SpotifyRequest? Build(string capabilityKey, object value, string deviceId, bool isActive)
    {
        var dev = new Dictionary<string, string?> { ["device_id"] = deviceId };
        switch (capabilityKey)
        {
            case "playback":
                if (!ToBool(value))
                    return new SpotifyRequest(HttpMethod.Put, "/me/player/pause", dev, null);
                if (isActive)
                    return new SpotifyRequest(HttpMethod.Put, "/me/player/play", dev, null);
                var body = JsonSerializer.Serialize(new { device_ids = new[] { deviceId }, play = true });
                return new SpotifyRequest(HttpMethod.Put, "/me/player", Empty, body);

            case "track":
                return value?.ToString()?.ToLowerInvariant() switch
                {
                    "next" => new SpotifyRequest(HttpMethod.Post, "/me/player/next", dev, null),
                    "previous" or "prev" => new SpotifyRequest(HttpMethod.Post, "/me/player/previous", dev, null),
                    _ => null,
                };

            case "volume":
                var pct = Math.Clamp(ToInt(value), 0, 100);
                return new SpotifyRequest(HttpMethod.Put, "/me/player/volume",
                    new Dictionary<string, string?> { ["volume_percent"] = pct.ToString(), ["device_id"] = deviceId }, null);

            default:
                return null;
        }
    }

    private static bool ToBool(object v) => v switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        double d => d != 0,
        long l => l != 0,
        _ => false,
    };

    private static int ToInt(object v) => v switch
    {
        double d => (int)Math.Round(d),
        int i => i,
        long l => (int)l,
        string s when double.TryParse(s, out var d) => (int)Math.Round(d),
        _ => 0,
    };
}
