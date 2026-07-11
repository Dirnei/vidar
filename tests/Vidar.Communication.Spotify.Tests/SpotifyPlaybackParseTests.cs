using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyPlaybackParseTests
{
    [Fact]
    public void Parse_Full_ExtractsActiveDevice_Playing_Volume_NowPlaying()
    {
        var json = """
        {"is_playing":true,"progress_ms":1000,
         "device":{"id":"a1","volume_percent":55},
         "item":{"name":"Song","duration_ms":200000,
           "artists":[{"name":"A"},{"name":"B"}],
           "album":{"name":"Alb","images":[{"url":"http://art"}]}}}
        """;
        var p = SpotifyStateMapper.Parse(json);
        Assert.Equal("a1", p.ActiveDeviceId);
        Assert.True(p.IsPlaying);
        Assert.Equal(55, p.ActiveVolumePercent);
        Assert.Equal("Song", p.NowPlaying["title"]);
        Assert.Equal("A, B", p.NowPlaying["artist"]);
        Assert.Equal("Alb", p.NowPlaying["album"]);
        Assert.Equal("http://art", p.NowPlaying["artUrl"]);
        Assert.Equal(1000d, p.NowPlaying["progressMs"]);
        Assert.Equal(200000d, p.NowPlaying["durationMs"]);
    }

    [Fact]
    public void Parse_Empty_IsIdle()
    {
        var p = SpotifyStateMapper.Parse("");
        Assert.Null(p.ActiveDeviceId);
        Assert.False(p.IsPlaying);
        Assert.Null(p.ActiveVolumePercent);
        Assert.Empty(p.NowPlaying);
    }
}
