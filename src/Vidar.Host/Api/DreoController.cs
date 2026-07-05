using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Dreo;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/dreo")]
public sealed class DreoController : ControllerBase
{
    private readonly IDreoAuth _auth;
    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;

    public DreoController(IDreoAuth auth, IApplicationConfigRepository repo,
        IRequiredActor<PluginRegistry> pluginRegistryProvider)
    {
        _auth = auth;
        _repo = repo;
        _pluginRegistryProvider = pluginRegistryProvider;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] DreoLoginRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _auth.PasswordLoginAsync(req.Email, req.Password, ct);
            await PersistAndNotify(req.Email, result);
            return Ok(result.Devices);
        }
        catch (HttpRequestException ex)
        {
            return ex.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests => Problem(
                    "Dreo temporarily rate-limited the request. Wait a few minutes and try again.",
                    statusCode: StatusCodes.Status429TooManyRequests),
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Problem(
                    "Dreo rejected the sign-in. Re-check the email/password and try again.",
                    statusCode: StatusCodes.Status401Unauthorized),
                _ => Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway),
            };
        }
    }

    [HttpGet("account")]
    public async Task<IActionResult> Account()
    {
        var cfg = await _repo.GetByIdAsync("dreo");
        if (cfg is null || !cfg.Enabled || !cfg.Settings.TryGetValue("account.email", out var email)
            || string.IsNullOrWhiteSpace(email))
            return Ok(new { connected = false });
        var count = 0;
        if (cfg.Settings.TryGetValue("account.manifest", out var m) && !string.IsNullOrWhiteSpace(m))
            try { count = JsonDocument.Parse(m).RootElement.GetArrayLength(); } catch { }
        return Ok(new { connected = true, email, deviceCount = count });
    }

    private async Task PersistAndNotify(string email, DreoAuthResult result)
    {
        var existing = await _repo.GetByIdAsync("dreo");
        var config = existing ?? new ApplicationConfig
        {
            Id = "dreo", Name = "Dreo", ApplicationType = ApplicationType.Provider,
        };
        config.Enabled = true;
        config.Settings = new Dictionary<string, string>
        {
            ["account.email"] = email,
            ["account.token"] = result.TokenJson,
            ["account.region"] = result.Region,
            ["account.manifest"] = JsonSerializer.Serialize(result.Devices.Select(d => new
            {
                serial = d.Serial, model = d.Model, name = d.Name,
            })),
        };
        await _repo.UpsertAsync(config);
        var pluginRegistry = await _pluginRegistryProvider.GetAsync();
        pluginRegistry.Tell(new RouteToPlugin("dreo",
            new IntegrationConfigChanged("dreo", config.Enabled, config.Settings)));
    }
}

public sealed record DreoLoginRequest(string Email, string Password);
