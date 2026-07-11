using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyVolumeStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"vol-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Save_Then_Load_RoundTrips()
    {
        var path = TempPath();
        var store = new SpotifyVolumeStore(path);
        try
        {
            await store.SaveAsync(new Dictionary<string, int> { ["dev-a"] = 42, ["dev-b"] = 0 });
            var loaded = await store.LoadAsync();
            Assert.Equal(42, loaded["dev-a"]);
            Assert.Equal(0, loaded["dev-b"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        var loaded = await new SpotifyVolumeStore(TempPath()).LoadAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task Load_CorruptFile_ReturnsEmpty()
    {
        var path = TempPath();
        await File.WriteAllTextAsync(path, "{not json");
        try { Assert.Empty(await new SpotifyVolumeStore(path).LoadAsync()); }
        finally { File.Delete(path); }
    }
}
