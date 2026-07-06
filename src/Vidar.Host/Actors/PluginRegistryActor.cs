using Akka.Actor;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

public sealed class PluginRegistryActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IDeviceRepository _deviceRepo;
    private readonly IApplicationConfigRepository _appRepo;
    private readonly Dictionary<string, IActorRef> _plugins = new();

    public static Props Props(IDeviceRepository deviceRepo, IApplicationConfigRepository appRepo) =>
        Akka.Actor.Props.Create(() => new PluginRegistryActor(deviceRepo, appRepo));

    public PluginRegistryActor(IDeviceRepository deviceRepo, IApplicationConfigRepository appRepo)
    {
        _deviceRepo = deviceRepo;
        _appRepo = appRepo;

        ReceiveAsync<RegisterPlugin>(HandleRegisterPlugin);

        Receive<RouteToPlugin>(msg =>
        {
            if (_plugins.TryGetValue(msg.PluginId, out var plugin))
                plugin.Forward(msg.Message);   // preserve original sender so Ask replies route back
            else
                _log.Warning("RouteToPlugin for '{PluginId}' dropped — not registered", msg.PluginId);
        });

        Receive<Terminated>(t =>
        {
            var removed = _plugins.Where(kv => kv.Value.Equals(t.ActorRef))
                .Select(kv => kv.Key).ToList();
            foreach (var key in removed)
                _plugins.Remove(key);
            if (removed.Count > 0)
                _log.Info("Plugin(s) terminated, removed: {Plugins}", string.Join(", ", removed));
        });
    }

    private async Task HandleRegisterPlugin(RegisterPlugin msg)
    {
        var previousExists = _plugins.TryGetValue(msg.PluginId, out var previous);
        if (previousExists && !previous!.Equals(msg.PluginActor))
        {
            _log.Info("Plugin '{PluginId}' re-registered by new actor, unwatching previous", msg.PluginId);
            if (!_plugins.Values.Any(v => v.Equals(previous) && _plugins.First(kv => kv.Value.Equals(v)).Key != msg.PluginId))
                Context.Unwatch(previous!);
        }

        _plugins[msg.PluginId] = msg.PluginActor;
        Context.Watch(msg.PluginActor);

        var config = await _appRepo.GetByIdAsync(msg.PluginId);
        var devices = await _deviceRepo.GetAllAsync();

        var registrations = BuildRegistrations(msg.PluginId, devices);

        msg.PluginActor.Tell(new PluginRegistered(
            msg.PluginId,
            config?.Enabled ?? false,
            config?.Settings ?? new Dictionary<string, string>(),
            registrations));

        _log.Info("Plugin '{PluginId}' registered with {RegCount} device registrations, enabled={Enabled}",
            msg.PluginId, registrations.Count, config?.Enabled ?? false);
    }

    private static List<RegisterDeviceForPolling> BuildRegistrations(
        string pluginId, IReadOnlyList<DeviceConfiguration> devices)
    {
        var registrations = new List<RegisterDeviceForPolling>();
        foreach (var d in devices)
        {
            if (d.CommunicationType != pluginId) continue;

            var host = d.Settings.GetValueOrDefault("host", "");
            var friendlyName = d.Settings.GetValueOrDefault("friendly_name", d.NativeId);
            int.TryParse(d.Settings.GetValueOrDefault("generation", "0"), out var generation);

            // The 4th arg (Host) carries the device's reachable address for plugins that need it.
            // Shelly uses "host"; all other plugins use the friendly name.
            var hostArg = pluginId == "shelly" ? host : friendlyName;

            registrations.Add(new RegisterDeviceForPolling(
                d.Id, d.CommunicationType, d.NativeId,
                hostArg,
                generation, d.Capabilities, d.Settings));
        }
        return registrations;
    }
}
