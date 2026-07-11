namespace Vidar.Communication.Spotify;

// A Spotify Connect device as reported by the Web API.
public sealed record SpotifyDevice(string Id, string Name, bool IsActive, int? VolumePercent);

// The parsed /me/player payload. NowPlaying is the card-shaped composite (title/artist/album/art/
// progressMs/durationMs); empty dictionary when nothing is playing.
public sealed record SpotifyPlayback(
    string? ActiveDeviceId,
    bool IsPlaying,
    int? ActiveVolumePercent,
    Dictionary<string, object> NowPlaying);
