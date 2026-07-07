using Vidar.Core.Plugins;

namespace Vidar.Communication.Spotify;

public sealed partial class SpotifyBridgeActor : PluginActorBase
{
    protected override string PluginId => "spotify";

    private readonly string _tokenFilePath;

    public static Akka.Actor.Props Props(string tokenFilePath) =>
        Akka.Actor.Props.Create(() => new SpotifyBridgeActor(tokenFilePath));

    public SpotifyBridgeActor(string tokenFilePath)
    {
        _tokenFilePath = tokenFilePath;
    }
}
