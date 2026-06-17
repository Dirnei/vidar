using Vidar.Core.Capabilities;

namespace Vidar.Core.Tests.Capabilities;

public sealed class UnitsTests
{
    [Theory]
    [InlineData(UnitType.Watts, ValueKind.Numeric)]
    [InlineData(UnitType.Celsius, ValueKind.Numeric)]
    [InlineData(UnitType.Percent, ValueKind.Numeric)]
    [InlineData(UnitType.OnOff, ValueKind.Boolean)]
    [InlineData(UnitType.OpenClosed, ValueKind.Boolean)]
    [InlineData(UnitType.Text, ValueKind.String)]
    public void KindOf_ReturnsCorrectValueKind(UnitType unit, ValueKind expected)
    {
        Assert.Equal(expected, Units.KindOf(unit));
    }

    [Fact]
    public void CapabilityDescriptor_ValueKind_DerivedFromUnit()
    {
        var cap = new CapabilityDescriptor { Key = "temp", Label = "Temperature", Unit = UnitType.Celsius };
        Assert.Equal(ValueKind.Numeric, cap.ValueKind);

        var sw = new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true };
        Assert.Equal(ValueKind.Boolean, sw.ValueKind);
    }

    [Theory]
    [InlineData(UnitType.Kilowatts, 1.5, 1500)]
    [InlineData(UnitType.KilowattHours, 2.0, 2000)]
    [InlineData(UnitType.Watts, 500, 500)]
    public void ToBase_ConvertsCorrectly(UnitType unit, double input, double expected)
    {
        Assert.Equal(expected, Units.ToBase(input, unit), 1);
    }

    [Theory]
    [InlineData(UnitType.Kilowatts, UnitType.Watts)]
    [InlineData(UnitType.KilowattHours, UnitType.WattHours)]
    [InlineData(UnitType.Watts, UnitType.Watts)]
    public void BaseOf_ReturnsCorrectBaseUnit(UnitType unit, UnitType expected)
    {
        Assert.Equal(expected, Units.BaseOf(unit));
    }

    [Fact]
    public void FormatNumeric_AutoScalesWattsToKilowatts()
    {
        Assert.Equal("1.5 kW", Units.FormatNumeric(1500, UnitType.Watts));
        Assert.Equal("500 W", Units.FormatNumeric(500, UnitType.Watts));
    }

    [Fact]
    public void FormatBoolean_UsesSemanticLabels()
    {
        Assert.Equal("On", Units.FormatBoolean(true, UnitType.OnOff));
        Assert.Equal("Off", Units.FormatBoolean(false, UnitType.OnOff));
        Assert.Equal("Open", Units.FormatBoolean(true, UnitType.OpenClosed));
        Assert.Equal("Closed", Units.FormatBoolean(false, UnitType.OpenClosed));
    }
}
