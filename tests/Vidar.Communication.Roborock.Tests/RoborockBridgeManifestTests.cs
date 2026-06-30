using Vidar.Communication.Roborock;
using Xunit;

public class RoborockBridgeManifestTests
{
    [Fact]
    public void ParsesManifestEntries()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [{"duid":"abc123","name":"Downstairs","model":"roborock.vacuum.a187",
              "localKey":"KEY","ip":"192.168.1.50"}]
            """,
        };
        var parsed = RoborockBridgeActor.ParseManifest(settings);
        var (cred, name) = Assert.Single(parsed);
        Assert.Equal("abc123", cred.Duid);
        Assert.Equal("roborock.vacuum.a187", cred.Model);
        Assert.Equal("KEY", cred.LocalKey);
        Assert.Equal("192.168.1.50", cred.Ip);
        Assert.Equal("Downstairs", name);
    }

    [Fact]
    public void SkipsEntriesWithEmptyModel()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """[{"duid":"x","name":"n","model":"","localKey":"k","ip":"1.2.3.4"}]""",
        };
        Assert.Empty(RoborockBridgeActor.ParseManifest(settings));
    }

    [Fact]
    public void MissingManifest_ReturnsEmpty()
    {
        Assert.Empty(RoborockBridgeActor.ParseManifest(new Dictionary<string, string>()));
    }

    [Fact]
    public void NameFallsBackToDuidWhenAbsent()
    {
        var settings = new Dictionary<string, string>
        {
            ["account.manifest"] = """
            [{"duid":"abc123","model":"roborock.vacuum.a187","localKey":"KEY","ip":"192.168.1.50"}]
            """,
        };
        var parsed = RoborockBridgeActor.ParseManifest(settings);
        var (cred, name) = Assert.Single(parsed);
        Assert.Equal("abc123", name);
        Assert.Equal("abc123", cred.Name);
    }
}
