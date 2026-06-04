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

    public static Props Props(IDiscoveredDeviceRepository repo) =>
        Akka.Actor.Props.Create(() => new DiscoveryManagerActor(repo));

    public DiscoveryManagerActor(IDiscoveredDeviceRepository repo)
    {
        ReceiveAsync<DeviceDiscovered>(async msg =>
        {
            var existing = await repo.GetByNativeIdAsync(msg.CommunicationType, msg.NativeId);
            if (existing != null) { _log.Debug("Device already discovered: {NativeId}", msg.NativeId); return; }
            var discovered = new DiscoveredDevice
            {
                Id = msg.DeviceId, CommunicationType = msg.CommunicationType, NativeId = msg.NativeId,
                Capabilities = msg.Capabilities.ToList(), Metadata = msg.Metadata, DiscoveredAt = DateTime.UtcNow
            };
            await repo.UpsertAsync(discovered);
            _log.Info("New device discovered: {CommunicationType}/{NativeId}", msg.CommunicationType, msg.NativeId);
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("device-discovered", Self));
    }
}
