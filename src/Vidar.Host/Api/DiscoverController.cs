using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/discover")]
public sealed class DiscoverController : ControllerBase
{
    private readonly ActorSystem _actorSystem;
    private readonly IDiscoveredDeviceRepository _discoveredRepo;
    private readonly ILogger<DiscoverController> _logger;

    public DiscoverController(ActorSystem actorSystem, IDiscoveredDeviceRepository discoveredRepo, ILogger<DiscoverController> logger)
    {
        _actorSystem = actorSystem;
        _discoveredRepo = discoveredRepo;
        _logger = logger;
    }

    [HttpPost("shelly")]
    public async Task<IActionResult> DiscoverShelly([FromBody] DiscoverShellyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { error = "Host is required" });

        _logger.LogInformation("Requesting Shelly discovery for host {Host}", request.Host);

        var countBefore = (await _discoveredRepo.GetAllAsync()).Count;
        var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
        mediator.Tell(new Publish("discover.shelly", new DiscoverShellyDevice(request.Host)));

        // Wait for the communication node to probe and report back
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var countAfter = (await _discoveredRepo.GetAllAsync()).Count;
            if (countAfter > countBefore)
            {
                _logger.LogInformation("Shelly device discovered at {Host}", request.Host);
                return Ok(new { status = "discovered", host = request.Host });
            }
        }

        _logger.LogWarning("Shelly device not found at {Host} after 5s timeout", request.Host);
        return Ok(new { status = "timeout", host = request.Host, message = "Device not found. Check the IP address and ensure the device is reachable from the Shelly communication node." });
    }
}

public sealed record DiscoverShellyRequest(string Host);
