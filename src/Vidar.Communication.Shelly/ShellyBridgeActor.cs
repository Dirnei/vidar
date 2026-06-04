using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using System.Text.Json;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;

namespace Vidar.Communication.Shelly;

public sealed class ShellyBridgeActor : ReceiveActor, IWithTimers
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ShellyHttpClient _httpClient;
    private readonly IActorRef _shardProxy;
    private readonly Dictionary<string, ShellyDevice> _devices = new();

    public ITimerScheduler Timers { get; set; } = null!;

    private sealed class PollTick { public static readonly PollTick Instance = new(); }

    public static Props Props(ShellyHttpClient httpClient, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new ShellyBridgeActor(httpClient, shardProxy));

    public ShellyBridgeActor(ShellyHttpClient httpClient, IActorRef shardProxy)
    {
        _httpClient = httpClient;
        _shardProxy = shardProxy;

        Receive<RegisterShellyDevice>(msg =>
        {
            _devices[msg.Device.NativeId] = msg.Device;
            _log.Info("Registered Shelly device: {NativeId} at {Host}", msg.Device.NativeId, msg.Device.Host);
        });

        Receive<PollTick>(_ => PollAllDevices());

        ReceiveAsync<DeviceCommand>(async cmd =>
        {
            var device = _devices.Values.FirstOrDefault(d => d.VidarDeviceId == cmd.DeviceId);
            if (device == null)
            {
                _log.Warning("No Shelly device found for DeviceId {DeviceId}", cmd.DeviceId);
                return;
            }

            try
            {
                if (cmd.Capability == CapabilityType.Switch && cmd.Value is bool on)
                    await _httpClient.SetSwitchAsync(device.Host, 0, on);
                else if (cmd.Capability == CapabilityType.Cover && cmd.Value is int pos)
                    await _httpClient.SetCoverPositionAsync(device.Host, 0, pos);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to execute command on Shelly device {Host}", device.Host);
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("commands.shelly", Self));
        Timers.StartPeriodicTimer("poll", PollTick.Instance, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void PollAllDevices()
    {
        foreach (var device in _devices.Values)
        {
            if (device.VidarDeviceId == null) continue;
            PollDeviceAsync(device).PipeTo(Self, failure: ex => new PollFailed(device.NativeId, ex));
        }
    }

    private async Task PollDeviceAsync(ShellyDevice device)
    {
        var doc = await _httpClient.GetStatusAsync(device.Host);
        if (doc == null) return;

        var root = doc.RootElement;
        var deviceId = device.VidarDeviceId!.Value;

        foreach (var cap in device.Capabilities)
        {
            List<ShellyCapabilityValue> updates;
            switch (cap)
            {
                case CapabilityType.Switch:
                case CapabilityType.Power:
                case CapabilityType.Energy:
                    if (root.TryGetProperty("switch:0", out var sw))
                    {
                        updates = ShellyStateMapper.MapSwitchStatus(sw);
                        SendUpdates(deviceId, updates);
                    }
                    break;
                case CapabilityType.Cover:
                    if (root.TryGetProperty("cover:0", out var cover))
                    {
                        updates = ShellyStateMapper.MapCoverStatus(cover);
                        SendUpdates(deviceId, updates);
                    }
                    break;
                case CapabilityType.Temperature:
                    if (root.TryGetProperty("temperature:0", out var temp))
                    {
                        updates = ShellyStateMapper.MapTemperatureStatus(temp);
                        SendUpdates(deviceId, updates);
                    }
                    break;
            }
        }
    }

    private void SendUpdates(Guid deviceId, List<ShellyCapabilityValue> updates)
    {
        foreach (var u in updates)
            _shardProxy.Tell(new DeviceStateUpdate(deviceId, u.Capability, u.Value));
    }

    private sealed record PollFailed(string NativeId, Exception Exception);
}

public sealed record RegisterShellyDevice(ShellyDevice Device);
