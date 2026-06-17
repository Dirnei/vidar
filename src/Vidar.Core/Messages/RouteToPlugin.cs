namespace Vidar.Core.Messages;

public sealed record RouteToPlugin(string PluginId, object Message);
