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

    public static Props Props(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo, IActorRef shardProxy) =>
        Akka.Actor.Props.Create(() => new DiscoveryManagerActor(discoveredRepo, deviceRepo, shardProxy));

    public DiscoveryManagerActor(IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo, IActorRef shardProxy)
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

        ReceiveAsync<DeviceManifestSnapshot>(async msg =>
        {
            var present = new HashSet<string>(msg.NativeIds, StringComparer.Ordinal);

            // Scope = devices of this CommunicationType whose NativeId is under this ScopeKey. For
            // Loxone the nativeId is "<serial>/<uuid>", so the scope prefix is "<ScopeKey>/".
            // Providers whose ScopeKey isn't a nativeId prefix can pass ScopeKey="" to scope by
            // CommunicationType alone.
            bool InScope(string nativeId) =>
                msg.ScopeKey.Length == 0 || nativeId.StartsWith(msg.ScopeKey + "/", StringComparison.Ordinal);

            // Prune discovered-but-absent
            var discovered = await discoveredRepo.GetAllAsync();
            foreach (var d in discovered)
            {
                if (d.CommunicationType != msg.CommunicationType) continue;
                if (!InScope(d.NativeId)) continue;
                if (present.Contains(d.NativeId)) continue;
                await discoveredRepo.DeleteAsync(d.Id);
                _log.Info("Retired discovered device {NativeId} (absent from {Type}/{Scope} manifest)",
                    d.NativeId, msg.CommunicationType, msg.ScopeKey);
            }

            // Mark accepted-but-absent offline
            var allDevices = await deviceRepo.GetAllAsync();
            foreach (var dev in allDevices)
            {
                if (dev.CommunicationType != msg.CommunicationType) continue;
                if (!InScope(dev.NativeId)) continue;
                if (present.Contains(dev.NativeId)) continue;
                shardProxy.Tell(new DeviceOffline(dev.Id));
                _log.Info("Marked accepted device {NativeId} offline (absent from manifest)", dev.NativeId);
            }
        });
    }

    // Structural fingerprint of a capability set — order-independent, covering every field that
    // affects how a capability is rendered or commanded, so any descriptor change triggers a re-sync.
    private static string CapabilitiesSignature(IEnumerable<Vidar.Core.Capabilities.CapabilityDescriptor> caps) =>
        string.Join(";", caps
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => $"{c.Key}|{c.Label}|{c.Unit}|{c.Commandable}|{c.Min}|{c.Max}|" +
                (c.Options is null ? "" : string.Join(",", c.Options.Select(o => $"{o.Value}:{o.Label}")))));

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("device-discovered", Self));
        mediator.Tell(new Subscribe("device-manifest", Self));
    }
}
