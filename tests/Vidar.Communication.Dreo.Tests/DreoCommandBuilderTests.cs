using System.Text.Json;
using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

public class DreoCommandBuilderTests
{
    private static JsonElement Params(string key, object value) =>
        JsonDocument.Parse(DreoCommandBuilder.Build(key, value)!).RootElement;

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
    public void Build_Mode_EmitsModeInt()
    {
        Assert.Equal(2, Params("mode", 2d).GetProperty("mode").GetInt32());
    }

    [Fact]
    public void Build_Light_Toggle_EmitsLightonBool()
    {
        Assert.True(Params("light", true).GetProperty("lighton").GetBoolean());
        Assert.False(Params("light", false).GetProperty("lighton").GetBoolean());
    }

    [Fact]
    public void Build_Light_Number_EmitsBrightnessInt()
    {
        // The composite light card sends a number for the brightness slider.
        Assert.Equal(75, Params("light", 75d).GetProperty("brightness").GetInt32());
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
