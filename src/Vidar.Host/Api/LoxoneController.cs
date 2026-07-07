using System.Net;
using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Loxone;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/loxone")]
public sealed class LoxoneController : ControllerBase
{
    private readonly ILoxoneSidecar _sidecar;
    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;

    public LoxoneController(ILoxoneSidecar sidecar, IApplicationConfigRepository repo,
        IRequiredActor<PluginRegistry> pluginRegistryProvider)
    {
        _sidecar = sidecar;
        _repo = repo;
        _pluginRegistryProvider = pluginRegistryProvider;
    }

    [HttpPost("miniservers")]
    public async Task<IActionResult> AddMiniserver([FromBody] LoxoneMiniserverRequest req, CancellationToken ct)
    {
        try
        {
            var probe = await _sidecar.ProbeAsync(req.Host, req.User, req.Password, ct);
            var existing = await _repo.GetByIdAsync("loxone");
            var miniservers = ReadMiniservers(existing)
                .Where(m => m.Serial != probe.Serial)
                .Append(new MiniserverEntry(probe.Serial, req.Host, req.User, req.Password))
                .ToList();
            await SaveAndNotify(existing, miniservers);
            return Ok(new { serial = probe.Serial, controlCount = probe.ControlCount, roomCount = probe.RoomCount });
        }
        catch (HttpRequestException ex)
        {
            return ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Problem(
                    "The Miniserver rejected the sign-in. Re-check the host/username/password and try again.",
                    statusCode: StatusCodes.Status401Unauthorized),
                _ => Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway),
            };
        }
    }

    [HttpGet("account")]
    public async Task<IActionResult> Account()
    {
        var cfg = await _repo.GetByIdAsync("loxone");
        var miniservers = ReadMiniservers(cfg);
        return Ok(new
        {
            connected = miniservers.Count > 0,
            miniservers = miniservers.Select(m => new { serial = m.Serial, host = m.Host }),
        });
    }

    [HttpDelete("miniservers/{serial}")]
    public async Task<IActionResult> RemoveMiniserver(string serial)
    {
        var cfg = await _repo.GetByIdAsync("loxone");
        var miniservers = ReadMiniservers(cfg);
        var remaining = miniservers.Where(m => m.Serial != serial).ToList();
        if (remaining.Count == miniservers.Count)
            return NotFound();
        await SaveAndNotify(cfg, remaining);
        return Ok(new { removed = serial });
    }

    private async Task SaveAndNotify(ApplicationConfig? existing, List<MiniserverEntry> miniservers)
    {
        var config = existing ?? new ApplicationConfig
        {
            Id = "loxone", Name = "Loxone", ApplicationType = ApplicationType.Provider,
        };
        config.Enabled = miniservers.Count > 0;
        config.Settings = new Dictionary<string, string>(config.Settings)
        {
            ["miniservers"] = JsonSerializer.Serialize(miniservers.Select(m => new
            {
                serial = m.Serial, host = m.Host, user = m.User, password = m.Password,
            })),
        };
        await _repo.UpsertAsync(config);
        var pluginRegistry = await _pluginRegistryProvider.GetAsync();
        pluginRegistry.Tell(new RouteToPlugin("loxone",
            new IntegrationConfigChanged("loxone", config.Enabled, config.Settings)));
    }

    // Mirrors LoxoneBridgeActor.ParseManifest on the worker side and
    // load_miniservers_from_mongo in the loxone2mqtt sidecar -- do not drift the field names.
    private static List<MiniserverEntry> ReadMiniservers(ApplicationConfig? cfg)
    {
        var result = new List<MiniserverEntry>();
        if (cfg is null || !cfg.Settings.TryGetValue("miniservers", out var json) || string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var serial = Str(e, "serial");
                var host = Str(e, "host");
                if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(host)) continue;
                result.Add(new MiniserverEntry(serial!, host!, Str(e, "user") ?? "", Str(e, "password") ?? ""));
            }
        }
        catch (JsonException) { }
        return result;

        static string? Str(JsonElement o, string p) =>
            o.TryGetProperty(p, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private sealed record MiniserverEntry(string Serial, string Host, string User, string Password);
}

public sealed record LoxoneMiniserverRequest(string Host, string User, string Password);
