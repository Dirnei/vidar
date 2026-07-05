using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/bambu")]
public sealed class BambuController : ControllerBase
{
    private const string PluginId = "bambu";

    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<PluginRegistry> _pluginRegistryProvider;

    public BambuController(IApplicationConfigRepository repo, IRequiredActor<PluginRegistry> pluginRegistryProvider)
    {
        _repo = repo;
        _pluginRegistryProvider = pluginRegistryProvider;
    }

    [HttpPost("printers")]
    public async Task<IActionResult> Add([FromBody] BambuPrinterRequest req)
    {
        var existing = await _repo.GetByIdAsync(PluginId);
        var manifest = UpsertManifest(existing?.Settings.GetValueOrDefault("account.manifest"), req);
        await PersistAndNotify(existing, manifest);
        return Ok(new { added = req.Serial });
    }

    [HttpGet("printers")]
    public async Task<IActionResult> List()
    {
        var cfg = await _repo.GetByIdAsync(PluginId);
        if (cfg is null || !cfg.Settings.TryGetValue("account.manifest", out var manifest)
            || string.IsNullOrWhiteSpace(manifest))
            return Ok(Array.Empty<object>());

        using var doc = JsonDocument.Parse(manifest);
        var printers = doc.RootElement.EnumerateArray().Select(e => new
        {
            host = e.GetProperty("host").GetString(),
            serial = e.GetProperty("serial").GetString(),
            model = e.TryGetProperty("model", out var m) ? m.GetString() : "",
            name = e.TryGetProperty("name", out var n) ? n.GetString() : "",
        }).ToList();
        return Ok(printers);
    }

    [HttpDelete("printers/{serial}")]
    public async Task<IActionResult> Delete(string serial)
    {
        var existing = await _repo.GetByIdAsync(PluginId);
        var list = new List<BambuPrinterRequest>();
        if (existing is not null && existing.Settings.TryGetValue("account.manifest", out var m)
            && !string.IsNullOrWhiteSpace(m))
        {
            using var doc = JsonDocument.Parse(m);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var entrySerial = e.GetProperty("serial").GetString()!;
                if (entrySerial == serial) continue;
                list.Add(new BambuPrinterRequest(
                    e.GetProperty("host").GetString()!, entrySerial,
                    e.GetProperty("accessCode").GetString()!,
                    e.TryGetProperty("model", out var mm) ? mm.GetString() ?? "" : "",
                    e.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : ""));
            }
        }

        await PersistAndNotify(existing, JsonSerializer.Serialize(list, CamelCase));
        return Ok(new { removed = serial });
    }

    // Pure helper: merges req into the existing manifest JSON, replacing any entry with the same serial.
    public static string UpsertManifest(string? existingJson, BambuPrinterRequest req)
    {
        var list = new List<BambuPrinterRequest>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            using var doc = JsonDocument.Parse(existingJson);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var serial = e.GetProperty("serial").GetString()!;
                if (serial == req.Serial) continue; // replaced below
                list.Add(new BambuPrinterRequest(
                    e.GetProperty("host").GetString()!, serial,
                    e.GetProperty("accessCode").GetString()!,
                    e.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                    e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));
            }
        }
        list.Add(req);
        return JsonSerializer.Serialize(list, CamelCase);
    }

    private async Task PersistAndNotify(ApplicationConfig? existing, string manifest)
    {
        var config = existing ?? new ApplicationConfig
        {
            Id = PluginId, Name = "Bambu Lab", ApplicationType = ApplicationType.Provider,
        };
        config.Enabled = true;
        config.Settings = new Dictionary<string, string>(config.Settings)
        {
            ["account.manifest"] = manifest,
        };
        await _repo.UpsertAsync(config);
        var pluginRegistry = await _pluginRegistryProvider.GetAsync();
        pluginRegistry.Tell(new RouteToPlugin(PluginId,
            new IntegrationConfigChanged(PluginId, config.Enabled, config.Settings)));
    }
}

public sealed record BambuPrinterRequest(string Host, string Serial, string AccessCode, string Model, string Name);
