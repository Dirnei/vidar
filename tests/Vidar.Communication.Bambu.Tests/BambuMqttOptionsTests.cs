using MQTTnet.Formatter;
using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuMqttOptionsTests
{
    [Fact]
    public void Build_SetsCredentialsAndV311()
    {
        var o = BambuMqttOptions.Build("192.168.1.50", 8883, "12345678");
        Assert.Equal(MqttProtocolVersion.V311, o.ProtocolVersion);
        Assert.Equal("bblp", o.Credentials!.GetUserName(o));
    }
}
