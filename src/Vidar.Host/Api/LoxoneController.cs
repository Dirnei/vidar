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
    private readonly IRoomMappingRepository _roomMappings;
    private readonly IDiscoveredDeviceRepository _discoveredRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IRoomRepository _roomRepo;

    public LoxoneController(ILoxoneSidecar sidecar, IApplicationConfigRepository repo,
        IRequiredActor<PluginRegistry> pluginRegistryProvider, IRoomMappingRepository roomMappings,
        IDiscoveredDeviceRepository discoveredRepo, IDeviceRepository deviceRepo, IRoomRepository roomRepo)
    {
        _sidecar = sidecar;
        _repo = repo;
        _pluginRegistryProvider = pluginRegistryProvider;
        _roomMappings = roomMappings;
        _discoveredRepo = discoveredRepo;
        _deviceRepo = deviceRepo;
        _roomRepo = roomRepo;
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

    [HttpGet("rooms")]
    public async Task<IActionResult> GetRooms()
    {
        var mappings = await _roomMappings.GetAllAsync();
        var vidarRooms = (await _roomRepo.GetAllAsync()).ToDictionary(r => r.Id, r => r.Name);

        // Distinct Loxone rooms from BOTH unaccepted (discovered) and accepted (device settings) loxone devices.
        var discovered = (await _discoveredRepo.GetAllAsync()).Where(d => d.CommunicationType == "loxone")
            .Select(d => (Serial: d.Metadata.GetValueOrDefault("serial", ""),
                          Uuid: d.Metadata.GetValueOrDefault("loxoneRoomUuid", ""),
                          Name: d.Metadata.GetValueOrDefault("loxoneRoomName", "")));
        var accepted = (await _deviceRepo.GetAllAsync()).Where(d => d.CommunicationType == "loxone")
            .Select(d => (Serial: d.Settings.GetValueOrDefault("serial", ""),
                          Uuid: d.Settings.GetValueOrDefault("loxoneRoomUuid", ""),
                          Name: d.Settings.GetValueOrDefault("loxoneRoomName", "")));

        var rooms = discovered.Concat(accepted)
            .Where(x => x.Serial.Length > 0 && x.Uuid.Length > 0)
            .GroupBy(x => (x.Serial, x.Uuid))
            .Select(g =>
            {
                var (serial, uuid) = g.Key;
                var name = g.Select(x => x.Name).FirstOrDefault(n => n.Length > 0) ?? uuid;
                var map = mappings.FirstOrDefault(m => m.PluginId == "loxone" && m.Serial == serial && m.ExternalRoomId == uuid);
                Guid? vidarId = map?.VidarRoomId;
                string? vidarName = vidarId is Guid id && vidarRooms.TryGetValue(id, out var n) ? n : null;
                return new { serial, roomUuid = uuid, roomName = name, vidarRoomId = vidarId, vidarRoomName = vidarName };
            })
            .OrderBy(r => r.roomName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(rooms);
    }

    [HttpPut("rooms/mapping")]
    public async Task<IActionResult> PutRoomMapping([FromBody] LoxoneRoomMappingRequest req)
    {
        Guid? vidarRoomId = req.VidarRoomId;

        if (!string.IsNullOrWhiteSpace(req.CreateRoomName))
        {
            var room = new RoomConfiguration { Id = Guid.NewGuid(), Name = req.CreateRoomName.Trim() };
            await _roomRepo.CreateAsync(room);
            vidarRoomId = room.Id;
        }

        // Null target with no create → unmap.
        if (vidarRoomId is null)
        {
            await _roomMappings.DeleteAsync("loxone", req.Serial, req.RoomUuid);
            return Ok(new { req.Serial, req.RoomUuid, vidarRoomId = (Guid?)null });
        }

        var existing = await _roomMappings.GetByExternalAsync("loxone", req.Serial, req.RoomUuid);
        var mapping = existing ?? new RoomMapping
        {
            Id = Guid.NewGuid(), PluginId = "loxone", Serial = req.Serial,
            ExternalRoomId = req.RoomUuid, ExternalRoomName = req.RoomName,
        };
        mapping.ExternalRoomName = req.RoomName;
        mapping.VidarRoomId = vidarRoomId;
        await _roomMappings.UpsertAsync(mapping);

        // Re-assign already-accepted loxone devices in this room to the mapped Vidar room.
        var devices = await _deviceRepo.GetAllAsync();
        foreach (var d in devices.Where(d => d.CommunicationType == "loxone"
            && d.Settings.GetValueOrDefault("serial", "") == req.Serial
            && d.Settings.GetValueOrDefault("loxoneRoomUuid", "") == req.RoomUuid))
        {
            d.RoomId = vidarRoomId.Value;
            await _deviceRepo.UpdateAsync(d);
        }

        return Ok(new { req.Serial, req.RoomUuid, vidarRoomId });
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

public sealed record LoxoneRoomMappingRequest(
    string Serial, string RoomUuid, string RoomName, Guid? VidarRoomId, string? CreateRoomName);
