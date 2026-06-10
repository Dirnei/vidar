using Vidar.Communication.UniFi.Webhooks;

namespace Vidar.Communication.UniFi.Tests.Webhooks;

public class WebhookParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    [Fact]
    public void ProtectParser_ParsesLicensePlateAlarm()
    {
        var evt = ProtectAlarmWebhookParser.Parse(Fixture("unifi_protect.json"));

        Assert.NotNull(evt);
        Assert.Equal("Kennzeichen", evt.AlarmName);
        Assert.Equal(2, evt.Triggers.Count);
        Assert.Equal("license_plate_known", evt.Triggers[0].Key);
        Assert.Equal("Mama", evt.Triggers[0].Value);
        Assert.Equal("942A6FD0A26B", evt.Triggers[0].Device);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1781077241154), evt.Timestamp);
    }

    [Fact]
    public void ProtectParser_NoAlarmProperty_ReturnsNull()
    {
        Assert.Null(ProtectAlarmWebhookParser.Parse("{}"));
    }

    [Fact]
    public void ProtectParser_NonStringTriggerValue_DoesNotThrow()
    {
        var json = """{"alarm":{"name":"Test","triggers":[{"device":"AABB","key":"motion","value":42}]},"timestamp":"not-a-number"}""";

        var evt = ProtectAlarmWebhookParser.Parse(json);

        Assert.NotNull(evt);
        Assert.Null(evt.Triggers[0].Value);
        Assert.Equal(DateTimeOffset.MinValue, evt.Timestamp);
    }

    [Fact]
    public void NetworkParser_ParsesClientDisconnectedEvent()
    {
        var evt = NetworkWebhookParser.Parse(Fixture("unifi_networks.json"));

        Assert.NotNull(evt);
        Assert.Equal("WiFi Client Disconnected", evt.Name);
        Assert.Contains("Samsung Android Phone", evt.Message);
        Assert.Equal("ba:5c:6e:84:23:77", evt.Parameters["UNIFIclientMac"]);
        Assert.Equal("Dirnhofer-AP", evt.Parameters["UNIFIwifiName"]);
    }

    [Fact]
    public void NetworkParser_MissingName_ReturnsNull()
    {
        Assert.Null(NetworkWebhookParser.Parse("{\"foo\":1}"));
    }
}
