using Vidar.Core.Capabilities;
using Xunit;

namespace Vidar.Core.Tests;

public class UnitsTests
{
    [Fact]
    public void Minutes_IsNumeric_WithMinSymbol()
    {
        Assert.Equal(ValueKind.Numeric, Units.KindOf(UnitType.Minutes));
        Assert.Equal("min", Units.Symbol(UnitType.Minutes));
    }
}
