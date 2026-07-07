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
        // Field keys as the live DR-HCF001S reports them (flattened data.mixed). mcuon is
        // present but intentionally not mapped (read-only, no `power` capability).
        var payload = """
        {"mcuon":true,"fanon":true,"windlevel":3,"mode":2,
         "lighton":true,"brightness":80,"colortemp":40}
        """;

        var u = Map(payload);

        Assert.False(u.ContainsKey("power"));
        Assert.Equal(true, u["fan"]);
        Assert.Equal(3d, u["fan_speed"]);
        Assert.Equal(2d, u["mode"]);
        // light is composite {on, brightness} for the unified card
        var light = Assert.IsType<Dictionary<string, object>>(u["light"]);
        Assert.Equal(true, light["on"]);
        Assert.Equal(80d, light["brightness"]);
        Assert.False(u.ContainsKey("light_brightness"));
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
        var u = Map("""{"lighton":false,"someFutureField":123}""");

        var light = Assert.IsType<Dictionary<string, object>>(u["light"]);
        Assert.Equal(false, light["on"]);
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
