using System.Text.Json;
using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

public class DreoCommandBuilderTests
{
    private static JsonElement Params(string key, object value) =>
        JsonDocument.Parse(DreoCommandBuilder.Build(key, value)!).RootElement;

    [Fact]
    public void Build_Power_EmitsPoweronBool()
    {
        Assert.True(Params("power", true).GetProperty("poweron").GetBoolean());
        Assert.False(Params("power", false).GetProperty("poweron").GetBoolean());
    }

    [Fact]
    public void Build_Fan_EmitsFanonBool()
    {
        Assert.True(Params("fan", true).GetProperty("fanon").GetBoolean());
    }

    [Fact]
    public void Build_FanSpeed_EmitsWindlevelInt()
    {
        Assert.Equal(3, Params("fan_speed", 3d).GetProperty("windlevel").GetInt32());
    }

    [Fact]
    public void Build_Mode_EmitsWindtypeString()
    {
        Assert.Equal("sleep", Params("mode", "sleep").GetProperty("windtype").GetString());
    }

    [Fact]
    public void Build_Light_EmitsLightonBool()
    {
        Assert.True(Params("light", true).GetProperty("lighton").GetBoolean());
    }

    [Fact]
    public void Build_Brightness_EmitsBrightnessInt()
    {
        Assert.Equal(75, Params("light_brightness", 75d).GetProperty("brightness").GetInt32());
    }

    [Fact]
    public void Build_ColorTemp_EmitsColortempInt()
    {
        Assert.Equal(40, Params("light_color_temp", 40d).GetProperty("colortemp").GetInt32());
    }

    [Fact]
    public void Build_UnknownCapability_ReturnsNull()
    {
        Assert.Null(DreoCommandBuilder.Build("nope", true));
    }
}
