using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonDeviceDiscoveryTests
{
    [Theory]
    [InlineData("X6p-EU-SKA0802A._dyson_mqtt._tcp.local", "X6p-EU-SKA0802A", true)]
    [InlineData("438_X6p-EU-SKA0802A._dyson_mqtt._tcp.local", "X6p-EU-SKA0802A", true)]
    [InlineData("OTHER-SERIAL._dyson_mqtt._tcp.local", "X6p-EU-SKA0802A", false)]
    public void MatchesSerial_FindsSerialInInstanceName(string instance, string serial, bool expected)
    {
        Assert.Equal(expected, DysonDeviceDiscovery.MatchesSerial(instance, serial));
    }

    [Fact]
    public async Task ResolveIp_ReturnsManualIpWithoutBrowsing()
    {
        var disc = new DysonDeviceDiscovery();
        var ip = await disc.ResolveIpAsync("any-serial", "192.168.5.157", TimeSpan.FromSeconds(1));
        Assert.Equal("192.168.5.157", ip);
    }
}
