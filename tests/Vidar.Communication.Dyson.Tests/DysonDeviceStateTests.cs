using Vidar.Communication.Dyson;

namespace Vidar.Communication.Dyson.Tests;

public class DysonDeviceStateTests
{
    [Theory]
    [InlineData(null, false, DysonTransport.NeedsConnection)]
    [InlineData("", false, DysonTransport.NeedsConnection)]
    [InlineData("192.168.5.157", false, DysonTransport.Offline)]   // ip set, not yet connected
    [InlineData("192.168.5.157", true, DysonTransport.Local)]      // ip set + connected
    public void Evaluate_DecidesTransport(string? ip, bool connected, DysonTransport expected)
    {
        Assert.Equal(expected, DysonDeviceState.Evaluate(ip, connected));
    }
}
