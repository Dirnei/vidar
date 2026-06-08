using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Messages;

namespace Vidar.Host.Actors;

public sealed class ApplicationStatusActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Dictionary<string, ApplicationStatusUpdate> _statuses = new();

    public sealed record GetAllStatuses;
    public sealed record GetStatus(string ApplicationId);
    public sealed record AllStatusesResponse(Dictionary<string, ApplicationStatusUpdate> Statuses);

    public static Props Props() => Akka.Actor.Props.Create(() => new ApplicationStatusActor());

    public ApplicationStatusActor()
    {
        Receive<ApplicationStatusUpdate>(msg =>
        {
            _statuses[msg.ApplicationId] = msg;
            _log.Debug("Status update for {App}: {Status}, devices={Count}",
                msg.ApplicationId, msg.Status, msg.DeviceCount);
        });

        Receive<GetAllStatuses>(_ =>
        {
            Sender.Tell(new AllStatusesResponse(new Dictionary<string, ApplicationStatusUpdate>(_statuses)));
        });

        Receive<GetStatus>(msg =>
        {
            _statuses.TryGetValue(msg.ApplicationId, out var status);
            Sender.Tell(status);
        });
    }

    protected override void PreStart()
    {
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("application-status", Self));
    }
}
