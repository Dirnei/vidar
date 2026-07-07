using System.Collections.Generic;
using System.Linq;
using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyDeviceListMapperTests
{
    private const string Devices = """
    { "devices": [
      { "id": "dev-kitchen", "name": "Kitchen", "is_active": true },
      { "id": "dev-living",  "name": "Living Room", "is_active": false }
    ] }
    """;

    private static object? Val(IReadOnlyList<(string, object)> r, string key) =>
        r.Where(t => t.Item1 == key).Select(t => t.Item2).FirstOrDefault();

    [Fact]
    public void MapsZonesList()
    {
        var zones = (List<Dictionary<string, object>>)Val(SpotifyDeviceListMapper.MapDevices(Devices), "zones")!;
        Assert.Equal(2, zones.Count);
        Assert.Equal("dev-kitchen", zones[0]["id"]);
        Assert.Equal("Kitchen", zones[0]["name"]);
        Assert.Equal(true, zones[0]["active"]);
        Assert.Equal(false, zones[1]["active"]);
    }

    [Fact]
    public void EmitsActiveZone()
    {
        Assert.Equal("dev-kitchen", Val(SpotifyDeviceListMapper.MapDevices(Devices), "zone"));
    }

    [Fact]
    public void EmptyOrNoActive_EmitsEmptyZonesNoActive()
    {
        var r = SpotifyDeviceListMapper.MapDevices("""{ "devices": [] }""");
        Assert.Empty((List<Dictionary<string, object>>)Val(r, "zones")!);
        Assert.Null(Val(r, "zone"));
    }

    [Fact]
    public void GarbageBody_EmptyZones()
    {
        var r = SpotifyDeviceListMapper.MapDevices("");
        Assert.Empty((List<Dictionary<string, object>>)Val(r, "zones")!);
    }
}
