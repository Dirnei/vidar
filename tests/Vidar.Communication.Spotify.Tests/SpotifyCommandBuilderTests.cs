using System.Net.Http;
using System.Text.Json;
using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyCommandBuilderTests
{
    [Fact]
    public void Playback_True_IsPlayPut()
    {
        var r = SpotifyCommandBuilder.Build("playback", true)!;
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/me/player/play", r.Path);
    }

    [Fact]
    public void Playback_False_IsPausePut()
    {
        var r = SpotifyCommandBuilder.Build("playback", false)!;
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/me/player/pause", r.Path);
    }

    [Fact]
    public void Track_Next_IsPost()
    {
        var r = SpotifyCommandBuilder.Build("track", "next")!;
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("/me/player/next", r.Path);
    }

    [Fact]
    public void Track_Previous_IsPost()
    {
        var r = SpotifyCommandBuilder.Build("track", "previous")!;
        Assert.Equal("/me/player/previous", r.Path);
    }

    [Fact]
    public void Volume_SetsVolumePercentQuery()
    {
        var r = SpotifyCommandBuilder.Build("volume", 55d)!;
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/me/player/volume", r.Path);
        Assert.Equal("55", r.Query["volume_percent"]);
    }

    [Fact]
    public void Volume_ClampsTo0_100()
    {
        Assert.Equal("100", SpotifyCommandBuilder.Build("volume", 150d)!.Query["volume_percent"]);
        Assert.Equal("0", SpotifyCommandBuilder.Build("volume", -5d)!.Query["volume_percent"]);
    }

    [Fact]
    public void Zone_TransfersToDeviceId()
    {
        var r = SpotifyCommandBuilder.Build("zone", "abc123")!;
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/me/player", r.Path);
        var body = JsonDocument.Parse(r.JsonBody!).RootElement;
        Assert.Equal("abc123", body.GetProperty("device_ids")[0].GetString());
        Assert.True(body.GetProperty("play").GetBoolean());
    }

    [Fact]
    public void UnknownCapability_ReturnsNull()
    {
        Assert.Null(SpotifyCommandBuilder.Build("nope", true));
    }
}
