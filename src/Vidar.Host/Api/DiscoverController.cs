using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/discover")]
public sealed class DiscoverController : ControllerBase
{
    private readonly ActorSystem _actorSystem;

    public DiscoverController(ActorSystem actorSystem)
    {
        _actorSystem = actorSystem;
    }

    [HttpPost("shelly")]
    public IActionResult DiscoverShelly([FromBody] DiscoverShellyRequest request)
    {
        var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
        mediator.Tell(new Publish("discover.shelly", new DiscoverShellyDevice(request.Host)));
        return Accepted();
    }
}

public sealed record DiscoverShellyRequest(string Host);
