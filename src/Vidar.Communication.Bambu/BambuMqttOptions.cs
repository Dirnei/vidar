using MQTTnet;
using MQTTnet.Formatter;

namespace Vidar.Communication.Bambu;

public static class BambuMqttOptions
{
    public static MqttClientOptions Build(string host, int port, string accessCode) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCredentials("bblp", accessCode)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTlsOptions(o =>
            {
                o.UseTls();
                // The printer presents a self-signed certificate; accept it (local LAN trust).
                o.WithCertificateValidationHandler(_ => true);
            })
            .Build();
}
