using Vidar.Communication.Loxone;
using Xunit;

public class LoxoneStateMapperTests
{
    [Fact]
    public void Switch_active_maps_to_switch_bool()
    {
        var r = LoxoneStateMapper.MapState("Switch", """{"active":1}""");
        Assert.Contains(r, u => u.CapabilityKey == "switch" && (bool)u.Value);
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
        var r = LoxoneStateMapper.MapState("LightControllerV2", """{"activeMood":778}""");
        Assert.Contains(r, u => u.CapabilityKey == "mode" && (double)u.Value == 778d);
    }

    [Fact]
    public void LightControllerV2_maps_position_to_dimmable_light()
    {
        // The sidecar folds the masterValue dimmer onto the parent: {active, position}.
        var r = LoxoneStateMapper.MapState("LightControllerV2", """{"active":true,"position":70}""");
        var light = Assert.Single(r, u => u.CapabilityKey == "light");
        var dict = Assert.IsType<Dictionary<string, object>>(light.Value);
        Assert.True((bool)dict["on"]);
        Assert.Equal(70d, dict["brightness"]);
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

    [Fact]
    public void ColorPickerRGBW_maps_light_color_white()
    {
        var r = LoxoneStateMapper.MapState("ColorPickerRGBW", """{"active":1,"position":80,"color":"#FF8800","white":25}""");
        var light = Assert.Single(r, u => u.CapabilityKey == "light");
        var dict = Assert.IsType<Dictionary<string, object>>(light.Value);
        Assert.True((bool)dict["on"]);
        Assert.Equal(80d, dict["brightness"]);
        Assert.Contains(r, u => u.CapabilityKey == "light_color" && (string)u.Value == "#FF8800");
        Assert.Contains(r, u => u.CapabilityKey == "light_white" && (double)u.Value == 25d);
    }

    [Fact]
    public void ColorPickerTunableWhite_maps_light_and_color_temp()
    {
        var r = LoxoneStateMapper.MapState("ColorPickerTunableWhite", """{"active":1,"position":60,"colortemp":3200}""");
        Assert.Contains(r, u => u.CapabilityKey == "light");
        Assert.Contains(r, u => u.CapabilityKey == "light_color_temp" && (double)u.Value == 3200d);
    }

    [Fact]
    public void RoomControllerV2_maps_climate_fields()
    {
        var r = LoxoneStateMapper.MapState("RoomControllerV2", """{"tempActual":21.5,"tempTarget":22,"mode":1,"valve":40}""");
        Assert.Contains(r, u => u.CapabilityKey == "temperature" && (double)u.Value == 21.5d);
        Assert.Contains(r, u => u.CapabilityKey == "target_temp" && (double)u.Value == 22d);
        Assert.Contains(r, u => u.CapabilityKey == "climate_mode" && (double)u.Value == 1d);
        Assert.Contains(r, u => u.CapabilityKey == "valve" && (double)u.Value == 40d);
    }
}
