using Vidar.Communication.Loxone;
using Xunit;

public class LoxoneCommandBuilderTests
{
    [Fact] public void Power_true_is_on()  => Assert.Equal("on",  LoxoneCommandBuilder.Build("power", true));
    [Fact] public void Power_false_is_off() => Assert.Equal("off", LoxoneCommandBuilder.Build("power", false));

    [Fact] public void Light_bool_true_is_on() => Assert.Equal("on", LoxoneCommandBuilder.Build("light", true));
    [Fact] public void Light_number_is_brightness_string() => Assert.Equal("55", LoxoneCommandBuilder.Build("light", 55d));

    [Fact] public void Mode_number_is_changeTo_mood_id() => Assert.Equal("changeTo/778", LoxoneCommandBuilder.Build("mode", 778d));

    [Fact] public void Light_color_emits_color_verb() => Assert.Equal("color/#FF8800", LoxoneCommandBuilder.Build("light_color", "#FF8800"));
    [Fact] public void Light_white_emits_white_verb() => Assert.Equal("white/25", LoxoneCommandBuilder.Build("light_white", 25d));
    [Fact] public void Color_temp_emits_temp_verb() => Assert.Equal("temp/3200", LoxoneCommandBuilder.Build("light_color_temp", 3200d));
    [Fact] public void Target_temp_emits_settemp_verb() => Assert.Equal("settemp/21.5", LoxoneCommandBuilder.Build("target_temp", 21.5d));
    [Fact] public void Climate_mode_emits_climatemode_verb() => Assert.Equal("climatemode/1", LoxoneCommandBuilder.Build("climate_mode", 1d));

    [Fact] public void Unknown_key_is_null() => Assert.Null(LoxoneCommandBuilder.Build("smoke", true));
}
