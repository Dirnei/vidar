using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyCapabilitiesTests
{
    [Fact]
    public void Build_HasExpectedKeys_NoZone()
    {
        var keys = SpotifyCapabilities.Build().Select(c => c.Key).ToHashSet();
        Assert.Contains("playback", keys);
        Assert.Contains("track", keys);
        Assert.Contains("volume", keys);
        Assert.Contains("now_playing", keys);
        Assert.Contains("active", keys);
        Assert.DoesNotContain("zone", keys);
    }

    [Fact]
    public void Active_IsReadOnly()
    {
        var active = SpotifyCapabilities.Build().Single(c => c.Key == "active");
        Assert.False(active.Commandable);
    }
}
