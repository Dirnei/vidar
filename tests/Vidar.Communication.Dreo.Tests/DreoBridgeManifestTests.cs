using Vidar.Communication.Dreo;
using Xunit;

namespace Vidar.Communication.Dreo.Tests;

public class DreoBridgeManifestTests
{
    [Fact]
    public void ParseManifest_ValidEntries_ReturnsCredentials()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [{"serial":"ABC123","model":"DR-HCF001S","name":"Living Room Fan"},
             {"serial":"DEF456","model":"DR-HCF002S"}]
            """,
        };

        var result = DreoBridgeActor.ParseManifest(settings);

        Assert.Equal(2, result.Count);
        Assert.Equal("ABC123", result[0].Cred.Serial);
        Assert.Equal("DR-HCF001S", result[0].Cred.Model);
        Assert.Equal("Living Room Fan", result[0].Name);
        Assert.Equal("DEF456", result[1].Cred.Serial);
        Assert.Equal("DEF456", result[1].Name); // falls back to serial
    }

    [Fact]
    public void ParseManifest_MalformedEntry_IsSkippedNotThrown()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [{"model":"DR-HCF001S"},{"serial":"OK1","model":"DR-HCF001S"}]
            """,
        };

        var result = DreoBridgeActor.ParseManifest(settings);

        Assert.Single(result);
        Assert.Equal("OK1", result[0].Cred.Serial);
    }

    [Fact]
    public void ParseManifest_MissingOrBlank_ReturnsEmpty()
    {
        Assert.Empty(DreoBridgeActor.ParseManifest(new Dictionary<string, string>()));
        Assert.Empty(DreoBridgeActor.ParseManifest(new Dictionary<string, string> { ["account.manifest"] = "" }));
    }
}
