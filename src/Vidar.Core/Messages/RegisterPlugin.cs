using Akka.Actor;

namespace Vidar.Core.Messages;

public sealed record RegisterPlugin(string PluginId, IActorRef PluginActor);

public sealed record PluginRegistered(
    string PluginId,
    bool Enabled,
    Dictionary<string, string> Settings,
    List<RegisterDeviceForPolling> Registrations);
