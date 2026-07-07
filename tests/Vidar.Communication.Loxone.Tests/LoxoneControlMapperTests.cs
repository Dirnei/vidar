using Vidar.Communication.Loxone;
using Vidar.Core.Capabilities;
using Xunit;

public class LoxoneControlMapperTests
{
    [Fact]
    public void Switch_maps_to_commandable_power()
    {
        var caps = LoxoneControlMapper.Map(new LoxoneControl("u1", "Kitchen Relay", "Switch", "r1", []));
        var power = Assert.Single(caps);
        Assert.Equal("power", power.Key);
        Assert.Equal(UnitType.OnOff, power.Unit);
        Assert.True(power.Commandable);
    }

    [Fact]
    public void Dimmer_maps_to_composite_light()
    {
        var caps = LoxoneControlMapper.Map(new LoxoneControl("u2", "Hall Dimmer", "Dimmer", "r1", []));
        var light = Assert.Single(caps);
        Assert.Equal("light", light.Key);
        Assert.Equal(UnitType.OnOff, light.Unit);
        Assert.True(light.Commandable);
    }

    [Fact]
    public void LightControllerV2_maps_to_power_plus_mode_with_mood_options()
    {
        var caps = LoxoneControlMapper.Map(new LoxoneControl("u3", "Living Light", "LightControllerV2", "r2",
            [new LoxoneMood(1, "Off"), new LoxoneMood(2, "Bright"), new LoxoneMood(778, "All On")]));
        Assert.Contains(caps, c => c.Key == "power" && c.Commandable);
        var mode = Assert.Single(caps, c => c.Key == "mode");
        Assert.NotNull(mode.Options);
        Assert.Equal(3, mode.Options!.Count);
        Assert.Contains(mode.Options!, o => o.Value == 778 && o.Label == "All On");
    }

    [Fact]
    public void PresenceDetector_maps_to_presence_and_brightness_readonly()
    {
        var caps = LoxoneControlMapper.Map(new LoxoneControl("u4", "Hall Presence", "PresenceDetector", "r2", []));
        Assert.Contains(caps, c => c.Key == "presence" && c.Unit == UnitType.Detected && !c.Commandable);
        Assert.Contains(caps, c => c.Key == "brightness" && c.Unit == UnitType.Lux && !c.Commandable);
    }

    [Fact]
    public void SmokeAlarm_maps_to_smoke_battery_tamper_readonly()
    {
        var caps = LoxoneControlMapper.Map(new LoxoneControl("u5", "Bedroom Smoke", "SmokeAlarm", "r3", []));
        Assert.Contains(caps, c => c.Key == "smoke" && c.Unit == UnitType.Detected && !c.Commandable);
        Assert.Contains(caps, c => c.Key == "battery" && c.Unit == UnitType.Percent);
        Assert.Contains(caps, c => c.Key == "tamper" && c.Unit == UnitType.Detected);
    }

    [Fact]
    public void Touch_maps_to_action_text()
    {
        var caps = LoxoneControlMapper.Map(new LoxoneControl("u6", "Kitchen Touch", "Touch", "r1", []));
        var action = Assert.Single(caps);
        Assert.Equal("action", action.Key);
        Assert.Equal(UnitType.Text, action.Unit);
    }

    [Fact]
    public void Unsupported_type_maps_to_empty()
    {
        Assert.Empty(LoxoneControlMapper.Map(new LoxoneControl("u7", "Blind", "Jalousie", "r1", [])));
    }
}
