using Vidar.Communication.Roborock;
using Vidar.Core.Capabilities;
using Xunit;

public class RoborockModelRegistryTests
{
    [Fact]
    public void DefaultProfile_ExposesVacuumCapabilities()
    {
        var caps = RoborockModelRegistry.Capabilities("roborock.vacuum.a187"); // Qrevo S Pro
        var keys = caps.Select(c => c.Key).ToHashSet();
        Assert.Contains("vacuum.state", keys);
        Assert.Contains("vacuum.battery", keys);
        Assert.Contains("vacuum.fanPower", keys);
        Assert.Contains("vacuum.start", keys);
        Assert.Contains("vacuum.cleanSegments", keys);
    }

    [Fact]
    public void StateAndBattery_AreNotCommandable()
    {
        var caps = RoborockModelRegistry.Capabilities("roborock.vacuum.a187");
        Assert.False(caps.Single(c => c.Key == "vacuum.state").Commandable);
        Assert.False(caps.Single(c => c.Key == "vacuum.battery").Commandable);
        Assert.True(caps.Single(c => c.Key == "vacuum.start").Commandable);
    }

    [Fact]
    public void EmptyModel_IsNotSupported()
    {
        Assert.False(RoborockModelRegistry.IsSupported(""));
        Assert.True(RoborockModelRegistry.IsSupported("roborock.vacuum.a187"));
    }

    [Fact]
    public void ExposesRoomsScenesAndRunScene()
    {
        var caps = RoborockModelRegistry.Capabilities("roborock.vacuum.a187");
        var byKey = caps.ToDictionary(c => c.Key);
        Assert.False(byKey["vacuum.rooms"].Commandable);
        Assert.False(byKey["vacuum.scenes"].Commandable);
        Assert.True(byKey["vacuum.runScene"].Commandable);
    }
}
