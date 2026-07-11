using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyDeviceParseTests
{
    [Fact]
    public void Parse_ExtractsId_Name_Active_Volume()
    {
        var json = """
        {"devices":[
          {"id":"a1","name":"Echo","is_active":true,"volume_percent":30},
          {"id":"b2","name":"PC","is_active":false,"volume_percent":80}
        ]}
        """;
        var devices = SpotifyDeviceListMapper.Parse(json);
        Assert.Equal(2, devices.Count);
        Assert.Equal("a1", devices[0].Id);
        Assert.Equal("Echo", devices[0].Name);
        Assert.True(devices[0].IsActive);
        Assert.Equal(30, devices[0].VolumePercent);
        Assert.False(devices[1].IsActive);
        Assert.Equal(80, devices[1].VolumePercent);
    }

    [Fact]
    public void Parse_MissingVolume_IsNull_NameFallsBackToId()
    {
        var json = """{"devices":[{"id":"a1","is_active":false}]}""";
        var d = Assert.Single(SpotifyDeviceListMapper.Parse(json));
        Assert.Equal("a1", d.Name);
        Assert.Null(d.VolumePercent);
    }

    [Fact]
    public void Parse_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(SpotifyDeviceListMapper.Parse(""));
        Assert.Empty(SpotifyDeviceListMapper.Parse("not json"));
        Assert.Empty(SpotifyDeviceListMapper.Parse("{}"));
    }
}
