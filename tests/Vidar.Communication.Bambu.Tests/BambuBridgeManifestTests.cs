using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuBridgeManifestTests
{
    [Fact]
    public void ParseManifest_ReadsAllPrinterFields()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [ {"host":"192.168.1.50","serial":"SER1","accessCode":"12345678","model":"BL-P001","name":"X1C"} ]
            """,
        };
        var printers = BambuBridgeActor.ParseManifest(settings);
        Assert.Single(printers);
        Assert.Equal("192.168.1.50", printers[0].Host);
        Assert.Equal("SER1", printers[0].Serial);
        Assert.Equal("12345678", printers[0].AccessCode);
        Assert.Equal("X1C", printers[0].Name);
    }

    [Fact]
    public void ParseManifest_MissingSetting_ReturnsEmpty() =>
        Assert.Empty(BambuBridgeActor.ParseManifest(new Dictionary<string, string>()));
}
