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
                // Re-sync when anything about the descriptors changed — not just the key set. A
                // capability can change its unit, commandable flag, or range between plugin
                // versions (e.g. a vacuum action moving from OnOff to Action); a key-only check
                // would leave the configured device on stale metadata.
                if (CapabilitiesSignature(msg.Capabilities) != CapabilitiesSignature(configured.Capabilities))
                {
                    configured.Capabilities.Clear();
                    configured.Capabilities.AddRange(msg.Capabilities);
                    await deviceRepo.UpdateAsync(configured);
                    _log.Info("Synced capabilities for configured device {NativeId}: {Caps}",
                        msg.NativeId, string.Join(",", msg.Capabilities.Select(c => c.Key)));
                }
            }
        });
    }

    // Structural fingerprint of a capability set — order-independent, covering every field that
    // affects how a capability is rendered or commanded, so any descriptor change triggers a re-sync.
    private static string CapabilitiesSignature(IEnumerable<Vidar.Core.Capabilities.CapabilityDescriptor> caps) =>
        string.Join(";", caps
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => $"{c.Key}|{c.Label}|{c.Unit}|{c.Commandable}|{c.Min}|{c.Max}"));

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("device-discovered", Self));
    }
}
