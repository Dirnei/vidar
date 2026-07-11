using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Persists last-known per-device volume ({spotifyDeviceId -> 0..100}) to a JSON file on the /data
// volume so a speaker shows the right level while offline and across restarts. Mirrors SpotifyTokenStore.
public sealed class SpotifyVolumeStore
{
    private readonly string _path;
    public SpotifyVolumeStore(string path) => _path = path;

    public async Task SaveAsync(IReadOnlyDictionary<string, int> volumes)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(volumes));
    }

    public async Task<Dictionary<string, int>> LoadAsync()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(await File.ReadAllTextAsync(_path)) ?? new();
        }
        catch (JsonException) { return new(); }
    }
}
