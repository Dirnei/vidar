using System.Net.Http;
using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Maps a standardized capability command to a Spotify Web API request spec.
// Present-capabilities only: unknown keys return null (ignored by the actor).
public static class SpotifyCommandBuilder
{
    private static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>();

    public static SpotifyRequest? Build(string capabilityKey, object value)
    {
        switch (capabilityKey)
        {
            case "playback":
                return ToBool(value)
                    ? new SpotifyRequest(HttpMethod.Put, "/me/player/play", Empty, null)
                    : new SpotifyRequest(HttpMethod.Put, "/me/player/pause", Empty, null);

            case "track":
                var dir = value?.ToString()?.ToLowerInvariant();
                return dir switch
                {
                    "next" => new SpotifyRequest(HttpMethod.Post, "/me/player/next", Empty, null),
                    "previous" or "prev" => new SpotifyRequest(HttpMethod.Post, "/me/player/previous", Empty, null),
                    _ => null,
                };

            case "volume":
                var pct = Math.Clamp(ToInt(value), 0, 100);
                return new SpotifyRequest(HttpMethod.Put, "/me/player/volume",
                    new Dictionary<string, string?> { ["volume_percent"] = pct.ToString() }, null);

            case "zone":
                var deviceId = value?.ToString();
                if (string.IsNullOrWhiteSpace(deviceId)) return null;
                var body = JsonSerializer.Serialize(new { device_ids = new[] { deviceId }, play = true });
                return new SpotifyRequest(HttpMethod.Put, "/me/player", Empty, body);

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
