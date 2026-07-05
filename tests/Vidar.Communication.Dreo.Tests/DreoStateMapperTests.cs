using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

public class DreoStateMapperTests
{
    private static Dictionary<string, object> Map(string payload) =>
        DreoStateMapper.MapState(payload).ToDictionary(x => x.CapabilityKey, x => x.Value);

    [Fact]
    public void MapState_FullFanAndLight_MapsAllKnownKeys()
    {
        var payload = """
        {"poweron":true,"fanon":true,"windlevel":3,"windtype":"normal",
         "lighton":true,"brightness":80,"colortemp":40}
        """;

        var u = Map(payload);

        Assert.Equal(true, u["power"]);
        Assert.Equal(true, u["fan"]);
        Assert.Equal(3d, u["fan_speed"]);
        Assert.Equal("normal", u["mode"]);
        Assert.Equal(true, u["light"]);
        Assert.Equal(80d, u["light_brightness"]);
        Assert.Equal(40d, u["light_color_temp"]);
    }

    [Fact]
    public void MapState_PartialPayload_MapsOnlyPresentKeys()
    {
        var u = Map("""{"windlevel":5}""");

        Assert.Equal(5d, u["fan_speed"]);
        Assert.False(u.ContainsKey("power"));
        Assert.False(u.ContainsKey("light"));
    }

    [Fact]
    public void MapState_UnknownKeys_AreIgnored()
    {
        var u = Map("""{"poweron":false,"someFutureField":123}""");

        Assert.Equal(false, u["power"]);
        Assert.Single(u);
    }

    [Fact]
    public void MapState_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(DreoStateMapper.MapState("{}"));
        Assert.Empty(DreoStateMapper.MapState(""));
        Assert.Empty(DreoStateMapper.MapState("not json"));
    }
}
