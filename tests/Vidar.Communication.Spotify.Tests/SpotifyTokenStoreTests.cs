using System;
using System.IO;
using System.Threading.Tasks;
using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyTokenStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spotify-tok-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SpotifyTokenStore(path);
            var exp = DateTimeOffset.UtcNow.AddHours(1);
            await store.SaveAsync(new SpotifyToken("acc", "ref", exp));
            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal("acc", loaded!.AccessToken);
            Assert.Equal("ref", loaded.RefreshToken);
            Assert.Equal(exp.ToUnixTimeSeconds(), loaded.ExpiresAt.ToUnixTimeSeconds());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsNull()
    {
        var store = new SpotifyTokenStore(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        Assert.Null(await store.LoadAsync());
    }
}
