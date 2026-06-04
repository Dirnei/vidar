using Vidar.Core.Capabilities;

namespace Vidar.Core.Tests.Capabilities;

public class CapabilityMetadataTests
{
    [Theory]
    [InlineData(CapabilityType.Switch, true)]
    [InlineData(CapabilityType.Dimmer, true)]
    [InlineData(CapabilityType.Cover, true)]
    [InlineData(CapabilityType.Temperature, false)]
    [InlineData(CapabilityType.Motion, false)]
    [InlineData(CapabilityType.Power, false)]
    [InlineData(CapabilityType.Energy, false)]
    [InlineData(CapabilityType.Humidity, false)]
    public void IsControllable_ReturnsCorrectValue(CapabilityType type, bool expected)
    {
        Assert.Equal(expected, CapabilityMetadata.IsControllable(type));
    }

    [Fact]
    public void AllCapabilityTypes_HaveMetadata()
    {
        foreach (var type in Enum.GetValues<CapabilityType>())
        {
            var unit = CapabilityMetadata.GetUnit(type);
            Assert.NotNull(unit);
        }
    }
}
