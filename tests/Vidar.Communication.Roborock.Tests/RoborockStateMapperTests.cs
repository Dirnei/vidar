using Vidar.Communication.Roborock;
using Xunit;

public class RoborockStateMapperTests
{
    [Fact]
    public void MapsBatteryFanAndDockedState()
    {
        var json = File.ReadAllText("Fixtures/qrevo-status.json");
        var updates = RoborockStateMapper.MapState(json).ToDictionary(u => u.CapabilityKey, u => u.Value);
        Assert.Equal("docked", updates["vacuum.state"]);
        Assert.Equal(87, Convert.ToInt32(updates["vacuum.battery"]));
        Assert.Equal(104, Convert.ToInt32(updates["vacuum.fanPower"]));
    }

    [Theory]
    [InlineData(5, "cleaning")]
    [InlineData(8, "docked")]
    [InlineData(10, "paused")]
    [InlineData(15, "returning")]
    [InlineData(12, "error")]
    [InlineData(3, "idle")]
    public void MapsStateCodes(int code, string expected)
    {
        var updates = RoborockStateMapper.MapState($"{{\"state\":{code},\"battery\":50}}")
            .ToDictionary(u => u.CapabilityKey, u => u.Value);
        Assert.Equal(expected, updates["vacuum.state"]);
    }

    [Fact]
    public void IgnoresControlFields()
    {
        var updates = RoborockStateMapper.MapState("{\"_transport\":\"local\",\"_rooms\":[]}");
        Assert.DoesNotContain(updates, u => u.CapabilityKey == "vacuum.battery");
    }
}
