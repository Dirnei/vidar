using System.Collections.Generic;
using System.Linq;
using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyStateMapperTests
{
    private const string Playing = """
    {
      "device": { "id": "dev-kitchen", "name": "Kitchen", "volume_percent": 42 },
      "is_playing": true,
      "progress_ms": 12000,
      "item": {
        "name": "Redshift",
        "duration_ms": 210000,
        "artists": [ { "name": "Deadmau5" } ],
        "album": { "name": "Random Album", "images": [ { "url": "http://img/large" }, { "url": "http://img/small" } ] }
      }
    }
    """;

    private static object? Val(IReadOnlyList<(string, object)> r, string key) =>
        r.Where(t => t.Item1 == key).Select(t => t.Item2).FirstOrDefault();

    [Fact]
    public void MapsPlaybackVolumeZone()
    {
        var r = SpotifyStateMapper.MapPlayer(Playing);
        Assert.Equal(true, Val(r, "playback"));
        Assert.Equal(42d, Val(r, "volume"));
        Assert.Equal("dev-kitchen", Val(r, "zone"));
    }

    [Fact]
    public void MapsNowPlayingComposite()
    {
        var np = (Dictionary<string, object>)Val(SpotifyStateMapper.MapPlayer(Playing), "now_playing")!;
        Assert.Equal("Redshift", np["title"]);
        Assert.Equal("Deadmau5", np["artist"]);
        Assert.Equal("Random Album", np["album"]);
        Assert.Equal("http://img/large", np["artUrl"]);
        Assert.Equal(12000d, np["progressMs"]);
        Assert.Equal(210000d, np["durationMs"]);
    }

    [Fact]
    public void EmptyBody_IsIdle()
    {
        var r = SpotifyStateMapper.MapPlayer("");
        Assert.Equal(false, Val(r, "playback"));
        var np = (Dictionary<string, object>)Val(r, "now_playing")!;
        Assert.Empty(np);
    }

    [Fact]
    public void MultipleArtists_Joined()
    {
        var json = """
        { "is_playing": false, "item": { "name": "X", "duration_ms": 1,
          "artists": [ {"name":"A"}, {"name":"B"} ], "album": { "name":"Y", "images": [] } } }
        """;
        var np = (Dictionary<string, object>)Val(SpotifyStateMapper.MapPlayer(json), "now_playing")!;
        Assert.Equal("A, B", np["artist"]);
        Assert.Equal("", np["artUrl"]);
    }
}
