using Akka.Actor;

namespace Vidar.Communication.Roborock;

public sealed partial class RoborockDeviceActor : ReceiveActor
{
    public static Props Props(RoborockDeviceCredential cred, Guid deviceId,
        string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new RoborockDeviceActor());
}
