using System.Text.Json;
using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonCommandBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_Power_EmitsFpwrStateSet()
    {
        var json = DysonCommandBuilder.Build("power", true, Now)!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("STATE-SET", doc.RootElement.GetProperty("msg").GetString());
        Assert.Equal("ON", doc.RootElement.GetProperty("data").GetProperty("fpwr").GetString());
    }

    [Fact]
    public void Build_FanSpeed_PadsToFourDigits()
    {
        var json = DysonCommandBuilder.Build("fan_speed", 5d, Now)!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("0005", doc.RootElement.GetProperty("data").GetProperty("fnsp").GetString());
    }

    [Fact]
    public void Build_ReadOnlySensor_ReturnsNull()
    {
        Assert.Null(DysonCommandBuilder.Build("pm25", 12d, Now));
    }

    [Fact]
    public void Build_Envelope_ContainsModeReasonWithHyphenNotUnderscore()
    {
        var json = DysonCommandBuilder.Build("power", true, Now)!;
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("mode-reason", out _),
            "JSON must contain 'mode-reason' (hyphen) property");
        Assert.False(doc.RootElement.TryGetProperty("mode_reason", out _),
            "JSON must NOT contain 'mode_reason' (underscore) property");
        Assert.Equal("LAPP", doc.RootElement.GetProperty("mode-reason").GetString());
    }
}
