using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Dyson;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/dyson")]
public sealed class DysonController : ControllerBase
{
    private readonly DysonCloudClient _cloud;
    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;

    public DysonController(DysonCloudClient cloud, IApplicationConfigRepository repo,
        IRequiredActor<PluginRegistry> pluginRegistryProvider)
    {
        _cloud = cloud;
        _repo = repo;
        _pluginRegistryProvider = pluginRegistryProvider;
    }

    [HttpPost("auth/begin")]
    public async Task<IActionResult> Begin([FromBody] BeginRequest req, CancellationToken ct)
    {
        try
        {
            var challengeId = await _cloud.BeginLoginAsync(req.Region, req.Email, ct);
            return Ok(new { challengeId });
        }
        catch (HttpRequestException ex)
        {
            return CloudError(ex);
        }
    }

    [HttpPost("auth/verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest req, CancellationToken ct)
    {
        try
        {
            var token = await _cloud.VerifyLoginAsync(req.Region, req.Email, req.Password, req.ChallengeId, req.Otp, ct);
            var devices = await _cloud.GetDevicesAsync(token, ct);
            return Ok(devices);
        }
        catch (HttpRequestException ex)
        {
            return CloudError(ex);
        }
    }

    // Surface upstream Dyson failures distinctly so the wizard can tell the user what to do,
    // instead of reporting every failure as a generic "cloud unavailable" 502.
    private IActionResult CloudError(HttpRequestException ex) => ex.StatusCode switch
    {
        System.Net.HttpStatusCode.TooManyRequests => Problem(
            "Dyson temporarily rate-limited the request. Wait a few minutes and try again.",
            statusCode: StatusCodes.Status429TooManyRequests),
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => Problem(
            "Dyson rejected the sign-in request. Re-check the email/region, or wait a moment and retry.",
            statusCode: StatusCodes.Status401Unauthorized),
        _ => Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway),
    };

    [HttpPost("devices")]
    public async Task<IActionResult> SaveDevices([FromBody] SaveDysonDevicesRequest req)
    {
        if (req.Devices is not { Count: > 0 }) return BadRequest("At least one device is required.");

        var existing = await _repo.GetByIdAsync("dyson");
        var config = existing ?? new ApplicationConfig
        {
            Id = "dyson",
            Name = "Dyson",
            ApplicationType = ApplicationType.Provider,
        };

        config.Enabled = true;
        config.Settings = new Dictionary<string, string>
        {
            ["devices"] = JsonSerializer.Serialize(req.Devices.Select(d => new
            {
                serial = d.Serial,
                productType = d.ProductType,
                mqttPassword = d.MqttPassword,
                ip = d.Ip,
            }))
        };

        await _repo.UpsertAsync(config);

        var pluginRegistry = await _pluginRegistryProvider.GetAsync();
        pluginRegistry.Tell(new RouteToPlugin("dyson",
            new IntegrationConfigChanged("dyson", config.Enabled, config.Settings)));

        return NoContent();
    }
}

public sealed class BeginRequest { public required string Region { get; set; } public required string Email { get; set; } }
public sealed class VerifyRequest
{
    public required string Region { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string ChallengeId { get; set; }
    public required string Otp { get; set; }
}
public sealed class SaveDysonDevicesRequest { public List<SaveDysonDevice> Devices { get; set; } = new(); }
public sealed class SaveDysonDevice
{
    public required string Serial { get; set; }
    public required string ProductType { get; set; }
    public required string MqttPassword { get; set; }
    public string? Ip { get; set; }
}
