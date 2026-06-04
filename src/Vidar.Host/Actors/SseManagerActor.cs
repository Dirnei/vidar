using System.Threading.Channels;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Vidar.Core.Messages;

namespace Vidar.Host.Actors;

public sealed record RegisterSseClient(Channel<DeviceStateChanged> Channel);
public sealed record UnregisterSseClient(Channel<DeviceStateChanged> Channel);

public sealed class SseManagerActor : ReceiveActor
{
    private readonly HashSet<Channel<DeviceStateChanged>> _clients = new();

    public static Props Props() => Akka.Actor.Props.Create(() => new SseManagerActor());

    public SseManagerActor()
    {
        Receive<DeviceStateChanged>(msg => { foreach (var client in _clients) client.Writer.TryWrite(msg); });
        Receive<RegisterSseClient>(msg => _clients.Add(msg.Channel));
        Receive<UnregisterSseClient>(msg => { _clients.Remove(msg.Channel); msg.Channel.Writer.TryComplete(); });
    }

    protected override void PreStart()
    {
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("device-state-changes", Self));
    }
}
