using System.Text.Json;
using Vidar.Communication.Roborock;
using Xunit;

public class RoborockCommandBuilderTests
{
    private static (string cap, JsonElement val) Parse(string? json)
    {
        var doc = JsonDocument.Parse(json!);
        return (doc.RootElement.GetProperty("capability").GetString()!,
                doc.RootElement.GetProperty("value"));
    }

    [Fact]
    public void BuildsStartCommand()
    {
        var (cap, _) = Parse(RoborockCommandBuilder.Build("vacuum.start", true));
        Assert.Equal("vacuum.start", cap);
    }

    [Fact]
    public void BuildsFanPowerWithNumericValue()
    {
        var (cap, val) = Parse(RoborockCommandBuilder.Build("vacuum.fanPower", 104));
        Assert.Equal("vacuum.fanPower", cap);
        Assert.Equal(104, val.GetInt32());
    }

    [Fact]
    public void BuildsCleanSegmentsWithCsv()
    {
        var (cap, val) = Parse(RoborockCommandBuilder.Build("vacuum.cleanSegments", "16,17"));
        Assert.Equal("vacuum.cleanSegments", cap);
        Assert.Equal("16,17", val.GetString());
    }

    [Fact]
    public void ReturnsNullForUnknownKey()
    {
        Assert.Null(RoborockCommandBuilder.Build("vacuum.bogus", true));
    }

    [Fact]
    public void BuildsRunSceneWithId()
    {
        var (cap, val) = Parse(RoborockCommandBuilder.Build("vacuum.runScene", 1234));
        Assert.Equal("vacuum.runScene", cap);
        Assert.Equal(1234, val.GetInt32());
    }
}
