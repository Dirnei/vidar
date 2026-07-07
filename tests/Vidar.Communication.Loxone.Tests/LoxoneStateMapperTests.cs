using Vidar.Communication.Loxone;
using Xunit;

public class LoxoneStateMapperTests
{
    [Fact]
    public void Switch_active_maps_to_power_bool()
    {
        var r = LoxoneStateMapper.MapState("Switch", """{"active":1}""");
        Assert.Contains(r, u => u.CapabilityKey == "power" && (bool)u.Value);
    }

    [Fact]
    public void Dimmer_maps_to_composite_light_on_and_brightness()
    {
        var r = LoxoneStateMapper.MapState("Dimmer", """{"active":1,"position":42}""");
        var light = Assert.Single(r, u => u.CapabilityKey == "light");
        var dict = Assert.IsType<Dictionary<string, object>>(light.Value);
        Assert.True((bool)dict["on"]);
        Assert.Equal(42d, dict["brightness"]);
    }

    [Fact]
    public void LightControllerV2_maps_activeMood_to_mode()
    {
        var r = LoxoneStateMapper.MapState("LightControllerV2", """{"activeMood":778,"active":1}""");
        Assert.Contains(r, u => u.CapabilityKey == "mode" && (double)u.Value == 778d);
        Assert.Contains(r, u => u.CapabilityKey == "power" && (bool)u.Value);
    }

    [Fact]
    public void Presence_maps_presence_and_brightness()
    {
        var r = LoxoneStateMapper.MapState("PresenceDetector", """{"active":1,"brightness":25}""");
        Assert.Contains(r, u => u.CapabilityKey == "presence" && (bool)u.Value);
        Assert.Contains(r, u => u.CapabilityKey == "brightness" && (double)u.Value == 25d);
    }

    [Fact]
    public void Smoke_maps_smoke_battery_tamper()
    {
        var r = LoxoneStateMapper.MapState("SmokeAlarm", """{"active":0,"battery":88,"tamper":0}""");
        Assert.Contains(r, u => u.CapabilityKey == "smoke" && !(bool)u.Value);
        Assert.Contains(r, u => u.CapabilityKey == "battery" && (double)u.Value == 88d);
        Assert.Contains(r, u => u.CapabilityKey == "tamper" && !(bool)u.Value);
    }

    [Fact]
    public void Touch_maps_action_string()
    {
        var r = LoxoneStateMapper.MapState("Touch", """{"action":"T1_click"}""");
        Assert.Contains(r, u => u.CapabilityKey == "action" && (string)u.Value == "T1_click");
    }

    [Fact]
    public void Unknown_type_or_bad_json_returns_empty()
    {
        Assert.Empty(LoxoneStateMapper.MapState("Jalousie", """{"position":0.5}"""));
        Assert.Empty(LoxoneStateMapper.MapState("Switch", "{bad"));
    }
}
