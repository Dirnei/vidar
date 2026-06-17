using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

public sealed class DiscoveryManagerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public static Props Props(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo) =>
        Akka.Actor.Props.Create(() => new DiscoveryManagerActor(discoveredRepo, deviceRepo));

    public DiscoveryManagerActor(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo)
    {
        ReceiveAsync<DeviceDiscovered>(async msg =>
        {
            var existing = await discoveredRepo.GetByNativeIdAsync(msg.CommunicationType, msg.NativeId);
            var discovered = new DiscoveredDevice
            {
                Id = existing?.Id ?? msg.DeviceId,
                CommunicationType = msg.CommunicationType,
                NativeId = msg.NativeId,
                Capabilities = msg.Capabilities.ToList(),
                Metadata = msg.Metadata,
                DiscoveredAt = existing?.DiscoveredAt ?? DateTime.UtcNow
            };
            await discoveredRepo.UpsertAsync(discovered);

            // Sync capabilities to already-configured devices
            var allDevices = await deviceRepo.GetAllAsync();
            var configured = allDevices.FirstOrDefault(d =>
                d.NativeId == msg.NativeId && d.CommunicationType == msg.CommunicationType);
            if (configured != null)
            {
                var existingKeys = configured.Capabilities.Select(c => c.Key).ToHashSet();
                var newCaps = msg.Capabilities.Where(c => !existingKeys.Contains(c.Key)).ToList();
                if (newCaps.Count > 0)
                {
                    configured.Capabilities.AddRange(newCaps);
                    await deviceRepo.UpdateAsync(configured);
                    _log.Info("Synced {Count} new capabilities to configured device {NativeId}: {Caps}",
                        newCaps.Count, msg.NativeId, string.Join(",", newCaps.Select(c => c.Key)));
                }
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("device-discovered", Self));
    }
}
