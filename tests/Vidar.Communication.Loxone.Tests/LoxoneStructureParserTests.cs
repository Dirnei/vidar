using Vidar.Communication.Loxone;
using Xunit;

public class LoxoneStructureParserTests
{
    private const string Json = """
    {
      "serial": "504F94A0",
      "controls": [
        {"uuid":"u1","name":"Kitchen Relay","type":"Switch","room":"r1"},
        {"uuid":"u3","name":"Living Light","type":"LightControllerV2","room":"r2",
         "moods":[{"id":1,"name":"Off"},{"id":778,"name":"All On"}]}
      ],
      "rooms":[{"uuid":"r1","name":"OG Kitchen"},{"uuid":"r2","name":"Living"}]
    }
    """;

    [Fact]
    public void Parses_controls_rooms_and_moods()
    {
        var s = LoxoneStructureParser.Parse(Json);
        Assert.NotNull(s);
        Assert.Equal("504F94A0", s!.Serial);
        Assert.Equal(2, s.Controls.Count);
        Assert.Equal("Switch", s.Controls[0].Type);
        Assert.Empty(s.Controls[0].Moods);
        var lc = s.Controls[1];
        Assert.Equal(2, lc.Moods.Count);
        Assert.Equal("All On", lc.Moods[1].Name);
        Assert.Equal("r2", lc.RoomUuid);
        Assert.Equal("OG Kitchen", s.Rooms[0].Name);
    }

    [Fact]
    public void Malformed_json_returns_null()
    {
        Assert.Null(LoxoneStructureParser.Parse("{not json"));
        Assert.Null(LoxoneStructureParser.Parse(""));
    }
}
