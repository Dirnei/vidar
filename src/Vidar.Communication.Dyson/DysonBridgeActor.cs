using Vidar.Core.Plugins;

namespace Vidar.Communication.Dyson;

public sealed class DysonBridgeActor : PluginActorBase
{
    protected override string PluginId => "dyson";

    public static Akka.Actor.Props Props() =>
        Akka.Actor.Props.Create(() => new DysonBridgeActor());

    public DysonBridgeActor()
    {
    }
}
