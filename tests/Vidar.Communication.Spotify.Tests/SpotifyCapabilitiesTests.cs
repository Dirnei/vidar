using Vidar.Communication.Spotify;
using Vidar.Core.Capabilities;
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

    [Fact]
    public void Volume_IsPercent_0To100_Commandable()
    {
        var volume = SpotifyCapabilities.Build().Single(c => c.Key == "volume");
        Assert.Equal(UnitType.Percent, volume.Unit);
        Assert.Equal(0, volume.Min);
        Assert.Equal(100, volume.Max);
        Assert.True(volume.Commandable);
    }

    [Fact]
    public void Track_IsAction_Commandable()
    {
        var track = SpotifyCapabilities.Build().Single(c => c.Key == "track");
        Assert.Equal(UnitType.Action, track.Unit);
        Assert.True(track.Commandable);
    }

    [Fact]
    public void NowPlaying_IsReadOnly()
    {
        var nowPlaying = SpotifyCapabilities.Build().Single(c => c.Key == "now_playing");
        Assert.False(nowPlaying.Commandable);
    }
}
