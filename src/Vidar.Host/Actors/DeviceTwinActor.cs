using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

public sealed class DeviceTwinActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _entityId;
    private readonly IDeviceStateRepository _stateRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly IActorRef _pluginRegistry;
    private DeviceConfiguration? _config;
    private readonly Dictionary<string, object> _states = new();

    public static Props Props(string entityId, IDeviceStateRepository stateRepo, IDeviceRepository deviceRepo, IHistoryRepository historyRepo, IActorRef pluginRegistry) =>
        Akka.Actor.Props.Create(() => new DeviceTwinActor(entityId, stateRepo, deviceRepo, historyRepo, pluginRegistry));

    public DeviceTwinActor(string entityId, IDeviceStateRepository stateRepo, IDeviceRepository deviceRepo, IHistoryRepository historyRepo, IActorRef pluginRegistry)
    {
        _entityId = entityId;
        _stateRepo = stateRepo;
        _deviceRepo = deviceRepo;
        _historyRepo = historyRepo;
        _pluginRegistry = pluginRegistry;

        ReceiveAsync<DeviceStateUpdate>(HandleStateUpdate);
        ReceiveAsync<DeviceCommand>(HandleCommand);
        ReceiveAsync<DeviceOffline>(HandleDeviceOffline);
    }

    protected override void PreStart()
    {
        base.PreStart();
        LoadConfigAsync().PipeTo(Self);
    }

    private async Task LoadConfigAsync()
    {
        if (Guid.TryParse(_entityId, out var deviceId))
        {
            _config = await _deviceRepo.GetByIdAsync(deviceId);
            var existingState = await _stateRepo.GetByDeviceIdAsync(deviceId);
            if (existingState != null)
            {
                foreach (var kvp in existingState.States)
                    _states[kvp.Key] = kvp.Value;
            }
        }
    }

    private async Task HandleStateUpdate(DeviceStateUpdate update)
    {
        _states[update.CapabilityKey] = update.Value;
        var now = DateTime.UtcNow;
        var state = new DeviceState
        {
            DeviceId = update.DeviceId,
            States = new Dictionary<string, object>(_states),
            LastUpdated = now,
            Online = true
        };
        try { await _stateRepo.UpsertAsync(state); }
        catch (Exception ex) { _log.Warning(ex, "Failed to persist state for device {DeviceId}", update.DeviceId); }

        // Write state history entry (fire-and-forget)
        _ = _historyRepo.AddStateEntryAsync(new StateHistoryEntry
        {
            DeviceId = update.DeviceId,
            Capability = update.CapabilityKey,
            Value = update.Value,
            Timestamp = now,
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _log.Warning(t.Exception, "Failed to write state history for device {DeviceId}", update.DeviceId);
        });

        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("device-state-changes", new DeviceStateChanged(update.DeviceId, update.CapabilityKey, update.Value, now)));
    }

    private async Task HandleDeviceOffline(DeviceOffline msg)
    {
        _log.Info("Device {DeviceId} is offline", msg.DeviceId);
        var state = new DeviceState
        {
            DeviceId = msg.DeviceId,
            States = new Dictionary<string, object>(_states),
            LastUpdated = DateTime.UtcNow,
            Online = false
        };
        try { await _stateRepo.UpsertAsync(state); }
        catch (Exception ex) { _log.Warning(ex, "Failed to persist offline state for device {DeviceId}", msg.DeviceId); }
    }

    private async Task HandleCommand(DeviceCommand command)
    {
        if (_config == null) { _log.Warning("Received command for unconfigured device {DeviceId}", command.DeviceId); return; }

        // Write command history entry (fire-and-forget)
        _ = _historyRepo.AddCommandEntryAsync(new CommandHistoryEntry
        {
            DeviceId = command.DeviceId,
            Capability = command.CapabilityKey,
            Value = command.Value,
            Source = "api",
            Timestamp = DateTime.UtcNow,
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
                _log.Warning(t.Exception, "Failed to write command history for device {DeviceId}", command.DeviceId);
        });

        _pluginRegistry.Tell(new RouteToPlugin(_config.CommunicationType, command));
    }
}
