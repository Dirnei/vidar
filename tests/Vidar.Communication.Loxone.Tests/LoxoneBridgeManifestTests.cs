using Vidar.Communication.Loxone;
using Xunit;

public class LoxoneBridgeManifestTests
{
    [Fact]
    public void Parses_multiple_miniservers()
    {
        var settings = new Dictionary<string, string>
        {
            ["miniservers"] = """
            [{"serial":"AAA","host":"192.168.1.10","user":"admin","password":"p1"},
             {"serial":"BBB","host":"192.168.1.11","user":"u2","password":"p2"}]
            """,
        };
        var list = LoxoneBridgeActor.ParseManifest(settings);
        Assert.Equal(2, list.Count);
        Assert.Equal("192.168.1.10", list[0].Host);
        Assert.Equal("BBB", list[1].Serial);
    }

    [Fact]
    public void Skips_entries_missing_serial_or_host()
    {
        var settings = new Dictionary<string, string>
        {
            ["miniservers"] = """[{"host":"192.168.1.10"},{"serial":"CCC","host":"192.168.1.12","user":"a","password":"b"}]""",
        };
        var list = LoxoneBridgeActor.ParseManifest(settings);
        Assert.Single(list);
        Assert.Equal("CCC", list[0].Serial);
    }

    [Fact]
    public void Missing_or_bad_manifest_returns_empty()
    {
        Assert.Empty(LoxoneBridgeActor.ParseManifest(new Dictionary<string, string>()));
        Assert.Empty(LoxoneBridgeActor.ParseManifest(new Dictionary<string, string> { ["miniservers"] = "{bad" }));
    }
}
