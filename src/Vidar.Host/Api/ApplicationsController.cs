using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Actors;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController : ControllerBase
{
    private readonly IApplicationConfigRepository _repo;
    private readonly ActorSystem _actorSystem;
    private readonly IRequiredActor<ApplicationStatusActor> _statusActorProvider;
    private readonly ILogger<ApplicationsController> _logger;

    private static readonly Dictionary<string, (string Name, string Type)> KnownApplications = new()
    {
        ["shelly"] = ("Shelly", "provider"),
        ["zigbee2mqtt"] = ("Zigbee2MQTT", "provider"),
        ["unifi"] = ("UniFi Network", "provider"),
    };

    public ApplicationsController(
        IApplicationConfigRepository repo,
        ActorSystem actorSystem,
        IRequiredActor<ApplicationStatusActor> statusActorProvider,
        ILogger<ApplicationsController> logger)
    {
        _repo = repo;
        _actorSystem = actorSystem;
        _statusActorProvider = statusActorProvider;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var configs = await _repo.GetAllAsync();
        var configMap = configs.ToDictionary(c => c.Id);

        var statusActor = await _statusActorProvider.GetAsync();
        var statusResponse = await statusActor.Ask<ApplicationStatusActor.AllStatusesResponse>(
            new ApplicationStatusActor.GetAllStatuses(), TimeSpan.FromSeconds(3));
        var statuses = statusResponse.Statuses;

        var results = new List<ApplicationResponse>();
        foreach (var (id, known) in KnownApplications)
        {
            configMap.TryGetValue(id, out var config);
            statuses.TryGetValue(id, out var status);

            results.Add(new ApplicationResponse(
                Id: id,
                Name: config?.Name ?? known.Name,
                Type: known.Type,
                Enabled: config?.Enabled ?? false,
                Status: status?.Status ?? "unconfigured",
                DeviceCount: status?.DeviceCount ?? 0,
                Settings: config?.Settings ?? new Dictionary<string, string>(),
                ErrorMessage: status?.ErrorMessage));
        }

        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        if (!KnownApplications.TryGetValue(id, out var known))
            return NotFound();

        var config = await _repo.GetByIdAsync(id);

        var statusActor = await _statusActorProvider.GetAsync();
        var status = await statusActor.Ask<ApplicationStatusUpdate?>(
            new ApplicationStatusActor.GetStatus(id), TimeSpan.FromSeconds(3));

        return Ok(new ApplicationResponse(
            Id: id,
            Name: config?.Name ?? known.Name,
            Type: known.Type,
            Enabled: config?.Enabled ?? false,
            Status: status?.Status ?? "unconfigured",
            DeviceCount: status?.DeviceCount ?? 0,
            Settings: config?.Settings ?? new Dictionary<string, string>(),
            ErrorMessage: status?.ErrorMessage));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateApplicationRequest request)
    {
        if (!KnownApplications.TryGetValue(id, out var known))
            return NotFound();

        var existing = await _repo.GetByIdAsync(id);
        var config = existing ?? new ApplicationConfig
        {
            Id = id,
            Name = known.Name,
            ApplicationType = known.Type == "provider" ? ApplicationType.Provider : ApplicationType.Consumer,
        };

        config.Enabled = request.Enabled;
        config.Settings = request.Settings ?? new Dictionary<string, string>();

        await _repo.UpsertAsync(config);

        var mediator = DistributedPubSub.Get(_actorSystem).Mediator;
        var changed = new IntegrationConfigChanged(id, config.Enabled, config.Settings);
        mediator.Tell(new Publish($"integration-config.{id}", changed));

        _logger.LogInformation("Updated application {Id}, enabled={Enabled}", id, config.Enabled);
        return NoContent();
    }
}

public sealed class UpdateApplicationRequest
{
    public bool Enabled { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}
