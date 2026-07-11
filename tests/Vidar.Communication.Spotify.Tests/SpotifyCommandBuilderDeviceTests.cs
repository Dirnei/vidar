using System.Net.Http;
using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyCommandBuilderDeviceTests
{
    [Fact]
    public void PlaybackTrue_Active_Plays_WithDeviceId()
    {
        var r = SpotifyCommandBuilder.Build("playback", true, "dev1", isActive: true)!;
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/me/player/play", r.Path);
        Assert.Equal("dev1", r.Query["device_id"]);
        Assert.Null(r.JsonBody);
    }

    [Fact]
    public void PlaybackTrue_Inactive_TransfersStream()
    {
        var r = SpotifyCommandBuilder.Build("playback", true, "dev1", isActive: false)!;
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/me/player", r.Path);
        Assert.Contains("\"device_ids\":[\"dev1\"]", r.JsonBody);
        Assert.Contains("\"play\":true", r.JsonBody);
    }

    [Fact]
    public void PlaybackFalse_Pauses_WithDeviceId()
    {
        var r = SpotifyCommandBuilder.Build("playback", false, "dev1", isActive: true)!;
        Assert.Equal("/me/player/pause", r.Path);
        Assert.Equal("dev1", r.Query["device_id"]);
    }

    [Theory]
    [InlineData("next", "/me/player/next")]
    [InlineData("previous", "/me/player/previous")]
    [InlineData("prev", "/me/player/previous")]
    public void Track_MapsDirection(string dir, string path)
    {
        var r = SpotifyCommandBuilder.Build("track", dir, "dev1", isActive: true)!;
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal(path, r.Path);
        Assert.Equal("dev1", r.Query["device_id"]);
    }

    [Fact]
    public void Volume_ClampsAndTargetsDevice()
    {
        var r = SpotifyCommandBuilder.Build("volume", 130, "dev1", isActive: true)!;
        Assert.Equal("/me/player/volume", r.Path);
        Assert.Equal("100", r.Query["volume_percent"]);
        Assert.Equal("dev1", r.Query["device_id"]);
    }

    [Fact]
    public void Volume_LongBoxedValue_NotZero() // regression: host boxes ints as long
    {
        var r = SpotifyCommandBuilder.Build("volume", 55L, "dev1", isActive: true)!;
        Assert.Equal("55", r.Query["volume_percent"]);
    }

    [Fact]
    public void UnknownKey_ReturnsNull()
    {
        Assert.Null(SpotifyCommandBuilder.Build("bogus", true, "dev1", isActive: true));
    }
}
