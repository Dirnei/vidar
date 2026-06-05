using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Capabilities;
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
    private DeviceConfiguration? _config;
    private readonly Dictionary<CapabilityType, object> _states = new();

    public static Props Props(string entityId, IDeviceStateRepository stateRepo, IDeviceRepository deviceRepo) =>
        Akka.Actor.Props.Create(() => new DeviceTwinActor(entityId, stateRepo, deviceRepo));

    public DeviceTwinActor(string entityId, IDeviceStateRepository stateRepo, IDeviceRepository deviceRepo)
    {
        _entityId = entityId;
        _stateRepo = stateRepo;
        _deviceRepo = deviceRepo;

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
        _states[update.Capability] = update.Value;
        var state = new DeviceState
        {
            DeviceId = update.DeviceId,
            States = new Dictionary<CapabilityType, object>(_states),
            LastUpdated = DateTime.UtcNow,
            Online = true
        };
        try { await _stateRepo.UpsertAsync(state); }
        catch (Exception ex) { _log.Warning(ex, "Failed to persist state for device {DeviceId}", update.DeviceId); }

        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("device-state-changes", new DeviceStateChanged(update.DeviceId, update.Capability, update.Value, DateTime.UtcNow)));
    }

    private async Task HandleDeviceOffline(DeviceOffline msg)
    {
        _log.Info("Device {DeviceId} is offline", msg.DeviceId);
        var state = new DeviceState
        {
            DeviceId = msg.DeviceId,
            States = new Dictionary<CapabilityType, object>(_states),
            LastUpdated = DateTime.UtcNow,
            Online = false
        };
        try { await _stateRepo.UpsertAsync(state); }
        catch (Exception ex) { _log.Warning(ex, "Failed to persist offline state for device {DeviceId}", msg.DeviceId); }

        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish("device-state-changes", new DeviceStateChanged(msg.DeviceId, CapabilityType.Switch, false, DateTime.UtcNow)));
    }

    private async Task HandleCommand(DeviceCommand command)
    {
        if (_config == null) { _log.Warning("Received command for unconfigured device {DeviceId}", command.DeviceId); return; }
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Publish($"commands.{_config.CommunicationType}", command));
    }
}
