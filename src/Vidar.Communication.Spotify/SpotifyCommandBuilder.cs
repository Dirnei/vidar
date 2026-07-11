using System.Net.Http;
using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Maps a standardized capability command to a Spotify Web API request spec.
// Present-capabilities only: unknown keys return null (ignored by the actor).
public static class SpotifyCommandBuilder
{
    private static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>();

    // Maps a capability command to a Spotify Web API request. `deviceId` targets a specific speaker;
    // when it is null/empty (the central "Spotify Player" acting on whatever is currently playing),
    // device_id is omitted so the command hits the active device. "Play here" (transfer) applies when
    // a non-null target is not the active device. The `zone` key transfers the stream to `value`.
    public static SpotifyRequest? Build(string capabilityKey, object value, string? deviceId, bool isActive)
    {
        var dev = string.IsNullOrEmpty(deviceId)
            ? Empty
            : new Dictionary<string, string?> { ["device_id"] = deviceId };
        switch (capabilityKey)
        {
            case "playback":
                if (!ToBool(value))
                    return new SpotifyRequest(HttpMethod.Put, "/me/player/pause", dev, null);
                // No target, or the target is already active → plain play. Otherwise transfer here.
                if (isActive || string.IsNullOrEmpty(deviceId))
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
                var q = new Dictionary<string, string?> { ["volume_percent"] = pct.ToString() };
                if (!string.IsNullOrEmpty(deviceId)) q["device_id"] = deviceId;
                return new SpotifyRequest(HttpMethod.Put, "/me/player/volume", q, null);

            case "zone":
                var target = value?.ToString();
                if (string.IsNullOrWhiteSpace(target)) return null;
                var transfer = JsonSerializer.Serialize(new { device_ids = new[] { target }, play = true });
                return new SpotifyRequest(HttpMethod.Put, "/me/player", Empty, transfer);

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
