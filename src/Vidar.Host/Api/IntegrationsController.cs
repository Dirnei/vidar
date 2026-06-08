using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IApplicationConfigRepository _repo;
    private readonly ActorSystem _actorSystem;
    private readonly ILogger<IntegrationsController> _logger;

    public IntegrationsController(
        IApplicationConfigRepository repo,
        ActorSystem actorSystem,
        ILogger<IntegrationsController> logger)
    {
        _repo = repo;
        _actorSystem = actorSystem;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var configs = await _repo.GetAllAsync();
        return Ok(configs);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var config = await _repo.GetByIdAsync(id);
        if (config == null) return NotFound();
        return Ok(config);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Upsert(string id, [FromBody] UpsertIntegrationRequest request)
    {
        var existing = await _repo.GetByIdAsync(id);
        var config = existing ?? new ApplicationConfig
        {
            Id = id,
            Name = id,
            ApplicationType = ApplicationType.Provider,
        };

        config.Enabled = request.Enabled;
        config.Settings = request.Settings ?? new Dictionary<string, string>();

        await _repo.UpsertAsync(config);

        // Notify the comm node via Distributed Pub/Sub
        var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
        var changed = new IntegrationConfigChanged(id, config.Enabled, config.Settings);
        mediator.Tell(new Publish($"integration-config.{id}", changed));

        _logger.LogInformation("Upserted integration config for {Id}, enabled={Enabled}", id, config.Enabled);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _repo.DeleteAsync(id);
        return NoContent();
    }
}

public sealed class UpsertIntegrationRequest
{
    public bool Enabled { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}
