using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Persistence;
using Vidar.Host.Roborock;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/roborock")]
public sealed class RoborockController : ControllerBase
{
    private readonly IRoborockAuth _auth;
    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;

    public RoborockController(IRoborockAuth auth, IApplicationConfigRepository repo,
        IRequiredActor<PluginRegistry> pluginRegistryProvider)
    {
        _auth = auth;
        _repo = repo;
        _pluginRegistryProvider = pluginRegistryProvider;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] RoborockLoginRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _auth.PasswordLoginAsync(req.Email, req.Password, ct);
            await PersistAndNotify(req.Email, result);
            return Ok(result.Devices);
        }
        catch (HttpRequestException ex)
        {
            return AuthError(ex);
        }
    }

    [HttpPost("request-code")]
    public async Task<IActionResult> RequestCode([FromBody] RoborockEmailRequest req, CancellationToken ct)
    {
        try
        {
            await _auth.RequestCodeAsync(req.Email, ct);
            return Ok();
        }
        catch (HttpRequestException ex)
        {
            return AuthError(ex);
        }
    }

    [HttpPost("code-login")]
    public async Task<IActionResult> CodeLogin([FromBody] RoborockCodeRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _auth.CodeLoginAsync(req.Email, req.Code, ct);
            await PersistAndNotify(req.Email, result);
            return Ok(result.Devices);
        }
        catch (HttpRequestException ex)
        {
            return AuthError(ex);
        }
    }

    [HttpGet("account")]
    public async Task<IActionResult> Account()
    {
        var cfg = await _repo.GetByIdAsync("roborock");
        if (cfg is null || !cfg.Enabled || !cfg.Settings.TryGetValue("account.email", out var email)
            || string.IsNullOrWhiteSpace(email))
            return Ok(new { connected = false });
        var count = 0;
        if (cfg.Settings.TryGetValue("account.manifest", out var m) && !string.IsNullOrWhiteSpace(m))
            try { count = JsonDocument.Parse(m).RootElement.GetArrayLength(); } catch { }
        return Ok(new { connected = true, email, deviceCount = count });
    }

    // Surface upstream Roborock failures distinctly so the wizard can tell the user what to do,
    // instead of reporting every failure as a generic "cloud unavailable" 502.
    private IActionResult AuthError(HttpRequestException ex) => ex.StatusCode switch
    {
        System.Net.HttpStatusCode.TooManyRequests => Problem(
            "Roborock temporarily rate-limited the request. Wait a few minutes and try again.",
            statusCode: StatusCodes.Status429TooManyRequests),
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Problem(
            "Roborock rejected the sign-in. Re-check the email/password (or code) and try again.",
            statusCode: StatusCodes.Status401Unauthorized),
        _ => Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway),
    };

    private async Task PersistAndNotify(string email, RoborockAuthResult result)
    {
        var existing = await _repo.GetByIdAsync("roborock");
        var config = existing ?? new ApplicationConfig
        {
            Id = "roborock", Name = "Roborock", ApplicationType = ApplicationType.Provider,
        };
        config.Enabled = true;
        config.Settings = new Dictionary<string, string>
        {
            ["account.email"] = email,
            ["account.userData"] = result.UserDataJson,
            ["account.manifest"] = JsonSerializer.Serialize(result.Devices.Select(d => new
            {
                duid = d.Duid, name = d.Name, model = d.Model, localKey = d.LocalKey, ip = d.Ip,
            })),
        };
        await _repo.UpsertAsync(config);
        var pluginRegistry = await _pluginRegistryProvider.GetAsync();
        pluginRegistry.Tell(new RouteToPlugin("roborock",
            new IntegrationConfigChanged("roborock", config.Enabled, config.Settings)));
    }
}

public sealed record RoborockLoginRequest(string Email, string Password);
public sealed record RoborockEmailRequest(string Email);
public sealed record RoborockCodeRequest(string Email, string Code);
