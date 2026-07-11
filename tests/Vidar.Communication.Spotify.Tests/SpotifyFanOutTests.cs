using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyFanOutTests
{
    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void ActiveDevice_GetsPlayingStateAndLiveVolume()
    {
        var playback = new SpotifyPlayback("a1", true, 55,
            new Dictionary<string, object> { ["title"] = "Song" });
        var devices = new List<SpotifyDevice> { new("a1", "Echo", true, 30) };
        var accepted = new Dictionary<string, Guid> { ["a1"] = G(1) };

        var outp = SpotifyFanOut.Build(playback, devices, accepted, new Dictionary<string, int>());
        var updates = Assert.Single(outp);
        Assert.Equal(G(1), updates.DeviceId);
        var map = updates.Updates.ToDictionary(u => u.Key, u => u.Value);
        Assert.Equal(true, map["active"]);
        Assert.Equal(true, map["playback"]);
        Assert.Equal(30, map["volume"]); // live device volume wins
        Assert.Equal("Song", ((Dictionary<string, object>)map["now_playing"])["title"]);
    }

    [Fact]
    public void InactiveDevice_IsNotPlaying_EmptyNowPlaying()
    {
        var playback = new SpotifyPlayback("a1", true, 55, new Dictionary<string, object> { ["title"] = "Song" });
        var devices = new List<SpotifyDevice> { new("a1", "Echo", true, 30), new("b2", "PC", false, 80) };
        var accepted = new Dictionary<string, Guid> { ["b2"] = G(2) };

        var updates = Assert.Single(SpotifyFanOut.Build(playback, devices, accepted, new Dictionary<string, int>()));
        var map = updates.Updates.ToDictionary(u => u.Key, u => u.Value);
        Assert.Equal(false, map["active"]);
        Assert.Equal(false, map["playback"]);
        Assert.Empty((Dictionary<string, object>)map["now_playing"]);
        Assert.Equal(80, map["volume"]);
    }

    [Fact]
    public void OfflineDevice_UsesPersistedVolume_NeverClobbersToZero()
    {
        var playback = new SpotifyPlayback("a1", true, 55, new Dictionary<string, object>());
        var devices = new List<SpotifyDevice> { new("a1", "Echo", true, 30) }; // b2 absent (offline)
        var accepted = new Dictionary<string, Guid> { ["b2"] = G(2) };
        var persisted = new Dictionary<string, int> { ["b2"] = 65 };

        var updates = Assert.Single(SpotifyFanOut.Build(playback, devices, accepted, persisted));
        var map = updates.Updates.ToDictionary(u => u.Key, u => u.Value);
        Assert.Equal(65, map["volume"]);
        Assert.Equal(false, map["active"]);
    }

    [Fact]
    public void OfflineDevice_NoPersistedVolume_OmitsVolumeTuple()
    {
        var playback = new SpotifyPlayback(null, false, null, new Dictionary<string, object>());
        var accepted = new Dictionary<string, Guid> { ["b2"] = G(2) };

        var updates = Assert.Single(SpotifyFanOut.Build(playback, new List<SpotifyDevice>(), accepted, new Dictionary<string, int>()));
        Assert.DoesNotContain(updates.Updates, u => u.Key == "volume");
    }
}
