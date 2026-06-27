using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonBridgeManifestTests
{
    [Fact]
    public void ParseManifest_ReadsDevices_SkipsRobots()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [
              {"serial":"X6P-EU-SKA0802A","productType":"358K","name":"Bedroom","mqttPassword":"pw"},
              {"serial":"ROBO-1","productType":"276","name":"Vac","mqttPassword":"pw2"}
            ]
            """,
        };

        var devices = DysonBridgeActor.ParseManifest(settings);

        Assert.Single(devices); // robot 276 excluded
        Assert.Equal("X6P-EU-SKA0802A", devices[0].Serial);
        Assert.Equal("358K", devices[0].ProductType);
        Assert.Null(devices[0].Ip); // no ip in settings yet
    }

    [Fact]
    public void ParseManifest_EmptyManifest_ReturnsEmpty()
    {
        var settings = new Dictionary<string, string>();
        var devices = DysonBridgeActor.ParseManifest(settings);
        Assert.Empty(devices);
    }

    [Fact]
    public void ParseManifest_AllRobots_ReturnsEmpty()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [
              {"serial":"ROBO-1","productType":"276","name":"Vac","mqttPassword":"pw"},
              {"serial":"ROBO-2","productType":"277","name":"Vac2","mqttPassword":"pw2"}
            ]
            """,
        };

        var devices = DysonBridgeActor.ParseManifest(settings);
        Assert.Empty(devices);
    }
}
