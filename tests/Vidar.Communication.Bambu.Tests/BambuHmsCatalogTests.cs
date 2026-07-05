using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuHmsCatalogTests
{
    [Fact]
    public void FormatCode_ZeroPadsHexInFourGroups()
    {
        Assert.Equal("HMS_0300_0100_0001_0004", BambuHmsCatalog.FormatCode(0x03000100, 0x00010004));
    }

    [Fact]
    public void Describe_UnknownCode_FallsBackToFormattedCode()
    {
        var s = BambuHmsCatalog.Describe(0x0EEEEEEE, 0x0EEEEEEE);
        Assert.StartsWith("HMS_", s);
    }

    [Fact]
    public void FilamentName_KnownId_ResolvesName_UnknownReturnsId()
    {
        Assert.Equal("PLA", BambuHmsCatalog.FilamentName("GFL99"));   // adjust to a real known id below
        Assert.Equal("ZZZZ", BambuHmsCatalog.FilamentName("ZZZZ"));
    }
}
