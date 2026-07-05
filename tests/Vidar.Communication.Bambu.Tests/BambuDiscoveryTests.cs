using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuDiscoveryTests
{
    [Fact]
    public void ParseSsdpNotify_ExtractsSerialModelHost()
    {
        const string notify =
            "NOTIFY * HTTP/1.1\r\n" +
            "Location: 192.168.1.50\r\n" +
            "USN: 00M09A1234567890\r\n" +
            "DevModel.bambu.com: BL-P001\r\n" +
            "DevName.bambu.com: MyX1C\r\n\r\n";
        var d = BambuDiscovery.ParseSsdpNotify(notify);
        Assert.NotNull(d);
        Assert.Equal("00M09A1234567890", d!.Serial);
        Assert.Equal("192.168.1.50", d.Host);
        Assert.Equal("BL-P001", d.Model);
    }

    [Fact]
    public void ParseSsdpNotify_NonBambu_ReturnsNull() =>
        Assert.Null(BambuDiscovery.ParseSsdpNotify("NOTIFY * HTTP/1.1\r\nLocation: 10.0.0.1\r\n\r\n"));
}
