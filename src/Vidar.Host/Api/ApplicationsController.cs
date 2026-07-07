using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Actors;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController : ControllerBase
{
    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<ApplicationStatusActor> _statusActorProvider;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;
    private readonly ILogger<ApplicationsController> _logger;

    public ApplicationsController(
        IApplicationConfigRepository repo,
        IRequiredActor<ApplicationStatusActor> statusActorProvider,
        IRequiredActor<PluginRegistry> pluginRegistryProvider,
        ILogger<ApplicationsController> logger)
    {
        _repo = repo;
        _statusActorProvider = statusActorProvider;
        _pluginRegistryProvider = pluginRegistryProvider;
        _logger = logger;
    }

    // Applications are discovered dynamically. The host holds NO hardcoded list of plugins:
    // an application exists if a plugin has announced itself on the `application-status`
    // pubsub (tracked by ApplicationStatusActor) or if it has a persisted config. Adding a
    // new integration therefore requires no change here — the plugin announces itself, the
    // host merely relays runtime state. Presentation (display name, icon, description, config
    // fields) lives in the frontend, which needs per-plugin config forms regardless.

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var configs = await _repo.GetAllAsync();
        var configMap = configs.ToDictionary(c => c.Id);

        // Per-device transport statuses are published as "plugin/serial" on the same topic;
        // application ids are bare slugs. Keep only application-level announcements.
        var statuses = (await GetStatusesAsync())
            .Where(kv => !kv.Key.Contains('/'))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var ids = configMap.Keys.Union(statuses.Keys);

        var results = ids
            .Select(id =>
            {
                configMap.TryGetValue(id, out var config);
                statuses.TryGetValue(id, out var status);
                return BuildResponse(id, config, status);
            })
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var config = await _repo.GetByIdAsync(id);

        var statusActor = await _statusActorProvider.GetAsync();
        var status = await statusActor.Ask<ApplicationStatusUpdate?>(
            new ApplicationStatusActor.GetStatus(id), TimeSpan.FromSeconds(3));

        // Unknown to both the registry and the config store ⇒ this application does not exist.
        if (config is null && status is null)
            return NotFound();

        return Ok(BuildResponse(id, config, status));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateApplicationRequest request)
    {
        var existing = await _repo.GetByIdAsync(id);
        var config = existing ?? new ApplicationConfig
        {
            Id = id,
            // Display name is a frontend concern; persist the id as a stable fallback.
            Name = id,
            ApplicationType = ApplicationType.Provider,
        };

        config.Enabled = request.Enabled;
        config.Settings = SettingsSecrets.PreserveRedacted(request.Settings ?? new Dictionary<string, string>(), existing?.Settings);

        await _repo.UpsertAsync(config);

        var pluginRegistry = await _pluginRegistryProvider.GetAsync();
        var changed = new IntegrationConfigChanged(id, config.Enabled, config.Settings);
        pluginRegistry.Tell(new RouteToPlugin(id, changed));

        _logger.LogInformation("Updated application {Id}, enabled={Enabled}", id, config.Enabled);
        return NoContent();
    }

    private async Task<Dictionary<string, ApplicationStatusUpdate>> GetStatusesAsync()
    {
        var statusActor = await _statusActorProvider.GetAsync();
        var response = await statusActor.Ask<ApplicationStatusActor.AllStatusesResponse>(
            new ApplicationStatusActor.GetAllStatuses(), TimeSpan.FromSeconds(3));
        return response.Statuses;
    }

    private static ApplicationResponse BuildResponse(
        string id, ApplicationConfig? config, ApplicationStatusUpdate? status) =>
        new(
            Id: id,
            Name: config?.Name ?? id,
            // The plugin's live announcement is authoritative for its kind; fall back to a
            // persisted config, then to provider. The host never classifies plugins itself.
            Type: (status?.Type ?? config?.ApplicationType ?? ApplicationType.Provider) == ApplicationType.Consumer
                ? "consumer"
                : "provider",
            Enabled: config?.Enabled ?? false,
            Status: status?.Status ?? "unconfigured",
            DeviceCount: status?.DeviceCount ?? 0,
            Settings: SettingsSecrets.Redact(config?.Settings ?? new Dictionary<string, string>()),
            ErrorMessage: status?.ErrorMessage);
}

public sealed class UpdateApplicationRequest
{
    public bool Enabled { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
}
