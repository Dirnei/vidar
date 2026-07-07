using System.Text.Json;

namespace Vidar.Communication.Spotify;

public sealed record SpotifyToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

// Persists the Spotify token to a JSON file on a mounted volume so it survives worker restarts
// (the file is never exposed via any API — unlike settings-persisted secrets).
public sealed class SpotifyTokenStore
{
    private readonly string _path;
    public SpotifyTokenStore(string path) => _path = path;

    private sealed record Dto(string accessToken, string? refreshToken, long expiresAtUnix);

    public async Task SaveAsync(SpotifyToken token)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var dto = new Dto(token.AccessToken, token.RefreshToken, token.ExpiresAt.ToUnixTimeSeconds());
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(dto));
    }

    public async Task<SpotifyToken?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(await File.ReadAllTextAsync(_path));
            if (dto is null || string.IsNullOrEmpty(dto.accessToken)) return null;
            return new SpotifyToken(dto.accessToken, dto.refreshToken,
                DateTimeOffset.FromUnixTimeSeconds(dto.expiresAtUnix));
        }
        catch (JsonException) { return null; }
    }
}
