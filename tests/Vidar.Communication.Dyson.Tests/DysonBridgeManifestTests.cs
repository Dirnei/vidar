using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonBridgeManifestTests
{
    [Fact]
    public void ParseManifest_ReadsSerialAndProductType_SkipsRobots()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [
              {"serial":"X6P-EU-SKA0802A","productType":"358K","name":"Bedroom"},
              {"serial":"ROBO-1","productType":"276","name":"Vac"}
            ]
            """,
        };

        var devices = DysonBridgeActor.ParseManifest(settings);

        Assert.Single(devices); // robot 276 excluded
        Assert.Equal("X6P-EU-SKA0802A", devices[0].Serial);
        Assert.Equal("358K", devices[0].ProductType);
    }

    [Fact]
    public void AccountToken_ReadsFromSettings()
    {
        var settings = new Dictionary<string, string> { ["account.token"] = "tok" };
        Assert.Equal("tok", DysonBridgeActor.AccountToken(settings));
        Assert.Null(DysonBridgeActor.AccountToken(new Dictionary<string, string>()));
    }
}
