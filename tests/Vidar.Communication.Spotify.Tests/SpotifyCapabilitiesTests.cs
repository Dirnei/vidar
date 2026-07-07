using System.Linq;
using Vidar.Communication.Spotify;
using Vidar.Core.Capabilities;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyCapabilitiesTests
{
    [Fact]
    public void BuildsExpectedKeys()
    {
        var keys = SpotifyCapabilities.Build().Select(c => c.Key).ToHashSet();
        Assert.Contains("playback", keys);
        Assert.Contains("track", keys);
        Assert.Contains("volume", keys);
        Assert.Contains("now_playing", keys);
        Assert.Contains("zone", keys);
    }

    [Fact]
    public void VolumeIsPercent0To100()
    {
        var vol = SpotifyCapabilities.Build().Single(c => c.Key == "volume");
        Assert.Equal(UnitType.Percent, vol.Unit);
        Assert.True(vol.Commandable);
        Assert.Equal(0, vol.Min);
        Assert.Equal(100, vol.Max);
    }

    [Fact]
    public void NowPlayingIsReadOnly()
    {
        Assert.False(SpotifyCapabilities.Build().Single(c => c.Key == "now_playing").Commandable);
    }

    [Fact]
    public void TrackIsAction()
    {
        Assert.Equal(UnitType.Action, SpotifyCapabilities.Build().Single(c => c.Key == "track").Unit);
    }
}
