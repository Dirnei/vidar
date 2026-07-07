using Vidar.Core.Plugins;

namespace Vidar.Communication.Loxone;

public sealed class LoxoneBridgeActor : PluginActorBase
{
    protected override string PluginId => "loxone";

    public static Akka.Actor.Props Props(string brokerHost, int brokerPort, string baseTopic) =>
        Akka.Actor.Props.Create(() => new LoxoneBridgeActor(brokerHost, brokerPort, baseTopic));

    public LoxoneBridgeActor(string brokerHost, int brokerPort, string baseTopic)
    {
        // Replaced in Task 5.
    }

    protected override void OnPluginRegistered(bool enabled,
        Dictionary<string, string> settings, List<Vidar.Core.Messages.RegisterDeviceForPolling> registrations)
        => PublishStatus("running", 0);
}
