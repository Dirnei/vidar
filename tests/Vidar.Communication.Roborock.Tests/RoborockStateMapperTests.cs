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
    public void MapsWasteWaterFullAndFreshWaterLowFromDockError()
    {
        // dockErrorStatus 39 = waste water tank full.
        var full = RoborockStateMapper.MapState("{\"state\":8,\"dockErrorStatus\":39}")
            .ToDictionary(u => u.CapabilityKey, u => u.Value);
        Assert.True((bool)full["vacuum.wasteWaterFull"]);
        Assert.False((bool)full["vacuum.freshWaterLow"]);

        // dockErrorStatus 38 = clean water tank empty -> fresh water low.
        var empty = RoborockStateMapper.MapState("{\"state\":8,\"dockErrorStatus\":38}")
            .ToDictionary(u => u.CapabilityKey, u => u.Value);
        Assert.False((bool)empty["vacuum.wasteWaterFull"]);
        Assert.True((bool)empty["vacuum.freshWaterLow"]);

        // Healthy dock reports both as false.
        var ok = RoborockStateMapper.MapState("{\"state\":8,\"dockErrorStatus\":0}")
            .ToDictionary(u => u.CapabilityKey, u => u.Value);
        Assert.False((bool)ok["vacuum.wasteWaterFull"]);
        Assert.False((bool)ok["vacuum.freshWaterLow"]);
    }

    [Fact]
    public void MapsFreshWaterLowFromWaterShortage()
    {
        var updates = RoborockStateMapper.MapState("{\"state\":8,\"waterShortageStatus\":1}")
            .ToDictionary(u => u.CapabilityKey, u => u.Value);
        Assert.True((bool)updates["vacuum.freshWaterLow"]);
        // No dock status present -> no waste-water reading.
        Assert.DoesNotContain("vacuum.wasteWaterFull", updates.Keys);
    }

    [Fact]
    public void OmitsWaterSensorsWhenAbsent()
    {
        var updates = RoborockStateMapper.MapState("{\"state\":8,\"battery\":50}")
            .Select(u => u.CapabilityKey).ToHashSet();
        Assert.DoesNotContain("vacuum.wasteWaterFull", updates);
        Assert.DoesNotContain("vacuum.freshWaterLow", updates);
    }

    [Fact]
    public void IgnoresControlFields()
    {
        var updates = RoborockStateMapper.MapState("{\"_transport\":\"local\",\"_rooms\":[]}");
        Assert.DoesNotContain(updates, u => u.CapabilityKey == "vacuum.battery");
    }

    [Fact]
    public void MapsRoomsAndScenes()
    {
        var json = """
        {"state":8,"battery":50,
         "_rooms":[{"id":16,"name":"Kitchen"},{"id":17,"name":"Hall"}],
         "_scenes":[{"id":1234,"name":"After dinner"}]}
        """;
        var updates = RoborockStateMapper.MapState(json).ToDictionary(u => u.CapabilityKey, u => u.Value);
        var rooms = Assert.IsAssignableFrom<System.Collections.IEnumerable>(updates["vacuum.rooms"]).Cast<object>().ToList();
        Assert.Equal(2, rooms.Count);
        var scenes = Assert.IsAssignableFrom<System.Collections.IEnumerable>(updates["vacuum.scenes"]).Cast<object>().ToList();
        Assert.Single(scenes);
    }

    [Fact]
    public void OmitsRoomsWhenEmptyOrAbsent()
    {
        var updates = RoborockStateMapper.MapState("{\"state\":8,\"_rooms\":[]}")
            .Select(u => u.CapabilityKey).ToHashSet();
        Assert.DoesNotContain("vacuum.rooms", updates);
        Assert.DoesNotContain("vacuum.scenes", updates);
    }
}
