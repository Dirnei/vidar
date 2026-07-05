using System.Text.Json;
using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuCommandBuilderTests
{
    private static JsonElement Print(string key, object v) =>
        JsonDocument.Parse(BambuCommandBuilder.Build(key, v)!).RootElement.GetProperty("print");

    [Fact]
    public void Pause_EmitsPauseCommand() =>
        Assert.Equal("pause", Print("print_pause", true).GetProperty("command").GetString());

    [Fact]
    public void NozzleTarget_EmitsM104Gcode() =>
        Assert.Equal("M104 S215\n", Print("nozzle_target", 215d).GetProperty("param").GetString());

    [Fact]
    public void BedTarget_EmitsM140Gcode() =>
        Assert.Equal("M140 S60\n", Print("bed_target", 60d).GetProperty("param").GetString());

    [Fact]
    public void FanCooling_PercentConvertsTo255Scale() =>
        Assert.Equal("M106 P1 S255\n", Print("fan_cooling", 100d).GetProperty("param").GetString());

    [Fact]
    public void ChamberLight_OnOff()
    {
        Assert.Equal("on", JsonDocument.Parse(BambuCommandBuilder.Build("light_chamber", true)!)
            .RootElement.GetProperty("system").GetProperty("led_mode").GetString());
        Assert.Equal("off", JsonDocument.Parse(BambuCommandBuilder.Build("light_chamber", false)!)
            .RootElement.GetProperty("system").GetProperty("led_mode").GetString());
    }

    [Fact]
    public void SpeedProfile_EmitsParam() =>
        Assert.Equal("2", Print("print_speed_profile", 2d).GetProperty("param").GetString());

    [Fact]
    public void CameraSnapshot_ReturnsNull_NotPublishedToPrinter() =>
        Assert.Null(BambuCommandBuilder.Build("camera_snapshot", true));

    [Fact]
    public void UnknownKey_ReturnsNull() =>
        Assert.Null(BambuCommandBuilder.Build("progress", 5d));
}
