using System.Text.Json;
using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonCommandBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_Power_EmitsFpwr()
    {
        using var doc = JsonDocument.Parse(DysonCommandBuilder.Build("power", true, Now)!);
        Assert.Equal("STATE-SET", doc.RootElement.GetProperty("msg").GetString());
        Assert.Equal("ON", doc.RootElement.GetProperty("data").GetProperty("fpwr").GetString());
        Assert.True(doc.RootElement.TryGetProperty("mode-reason", out _));
    }

    [Fact]
    public void Build_FanSpeed_PadsToFourDigits()
    {
        using var doc = JsonDocument.Parse(DysonCommandBuilder.Build("fan_speed", 5d, Now)!);
        Assert.Equal("0005", doc.RootElement.GetProperty("data").GetProperty("fnsp").GetString());
    }

    [Fact]
    public void Build_Humidify_EmitsHumeHumdOrOff()
    {
        using var on = JsonDocument.Parse(DysonCommandBuilder.Build("humidify", true, Now)!);
        Assert.Equal("HUMD", on.RootElement.GetProperty("data").GetProperty("hume").GetString());
        using var off = JsonDocument.Parse(DysonCommandBuilder.Build("humidify", false, Now)!);
        Assert.Equal("OFF", off.RootElement.GetProperty("data").GetProperty("hume").GetString());
    }

    [Fact]
    public void Build_TargetHumidity_PadsToFourDigits()
    {
        using var doc = JsonDocument.Parse(DysonCommandBuilder.Build("target_humidity", 50d, Now)!);
        Assert.Equal("0050", doc.RootElement.GetProperty("data").GetProperty("humt").GetString());
    }

    [Fact]
    public void Build_ReadOnlySensor_ReturnsNull()
    {
        Assert.Null(DysonCommandBuilder.Build("pm25", 12d, Now));
    }
}
