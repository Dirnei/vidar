using Vidar.Communication.Loxone;
using Xunit;

public class LoxoneCommandBuilderTests
{
    [Fact] public void Power_true_is_on()  => Assert.Equal("on",  LoxoneCommandBuilder.Build("power", true));
    [Fact] public void Power_false_is_off() => Assert.Equal("off", LoxoneCommandBuilder.Build("power", false));

    [Fact] public void Light_bool_true_is_on() => Assert.Equal("on", LoxoneCommandBuilder.Build("light", true));
    [Fact] public void Light_number_is_brightness_string() => Assert.Equal("55", LoxoneCommandBuilder.Build("light", 55d));

    [Fact] public void Mode_number_is_changeTo_mood_id() => Assert.Equal("changeTo/778", LoxoneCommandBuilder.Build("mode", 778d));

    [Fact] public void Unknown_key_is_null() => Assert.Null(LoxoneCommandBuilder.Build("smoke", true));
}
