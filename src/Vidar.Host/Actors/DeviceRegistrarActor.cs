using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Vidar.Core.Messages;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

public sealed class DeviceRegistrarActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public static Props Props(IDeviceRepository deviceRepo) =>
        Akka.Actor.Props.Create(() => new DeviceRegistrarActor(deviceRepo));

    public DeviceRegistrarActor(IDeviceRepository deviceRepo)
    {
        ReceiveAsync<RequestRegistrations>(async msg =>
        {
            var devices = await deviceRepo.GetAllAsync();
            var registrations = new List<RegisterDeviceForPolling>();

            foreach (var d in devices)
            {
                if (d.CommunicationType != msg.CommunicationType) continue;

                if (d.CommunicationType == "shelly")
                {
                    if (!d.Settings.TryGetValue("host", out var host)) continue;
                    int.TryParse(d.Settings.GetValueOrDefault("generation", "2"), out var generation);
                    registrations.Add(new RegisterDeviceForPolling(
                        d.Id, d.CommunicationType, d.NativeId, host, generation, d.Capabilities));
                }
                else if (d.CommunicationType == "zigbee2mqtt")
                {
                    var friendlyName = d.Settings.GetValueOrDefault("friendly_name", d.NativeId);
                    registrations.Add(new RegisterDeviceForPolling(
                        d.Id, d.CommunicationType, d.NativeId, friendlyName, 0, d.Capabilities));
                }
            }

            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Publish($"registration-response.{msg.CommunicationType}", new RegistrationResponse(registrations)));
            _log.Info("Published {Count} {Type} registrations", registrations.Count, msg.CommunicationType);
        });
    }

    protected override void PreStart()
    {
        var mediator = DistributedPubSub.Get(Context.System).Mediator;
        mediator.Tell(new Subscribe("request-registrations", Self));
    }
}
