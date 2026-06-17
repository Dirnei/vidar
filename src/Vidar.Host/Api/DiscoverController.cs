using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Capabilities;
using Vidar.Core.Model;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/discover")]
public sealed class DiscoverController : ControllerBase
{
    private readonly IDiscoveredDeviceRepository _discoveredRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscoverController> _logger;

    public DiscoverController(
        IDiscoveredDeviceRepository discoveredRepo,
        IDeviceRepository deviceRepo,
        IHttpClientFactory httpClientFactory,
        ILogger<DiscoverController> logger)
    {
        _discoveredRepo = discoveredRepo;
        _deviceRepo = deviceRepo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost("shelly")]
    public async Task<IActionResult> DiscoverShelly([FromBody] DiscoverShellyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { error = "Host is required" });

        var host = request.Host.Trim();
        _logger.LogInformation("Probing Shelly device at {Host}", host);

        var http = _httpClientFactory.CreateClient("shelly");
        int generation;
        JsonDocument shellyDoc;
        JsonDocument? statusDoc;

        // Probe /shelly first — works on both Gen1 and Gen2
        try
        {
            var shellyResponse = await http.GetAsync($"http://{host}/shelly");
            shellyResponse.EnsureSuccessStatusCode();
            shellyDoc = await JsonDocument.ParseAsync(await shellyResponse.Content.ReadAsStreamAsync());
            generation = shellyDoc.RootElement.TryGetProperty("gen", out var genProp) ? genProp.GetInt32() : 1;
            _logger.LogInformation("Shelly device at {Host} is Gen{Gen}", host, generation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach Shelly device at {Host}", host);
            return Ok(new { status = "error", host, message = $"Cannot reach device at {host}. Check the IP address and ensure the device is powered on." });
        }

        // Get full status — different endpoint per generation
        var statusUrl = generation >= 2
            ? $"http://{host}/rpc/Shelly.GetStatus"
            : $"http://{host}/status";
        try
        {
            var statusResponse = await http.GetAsync(statusUrl);
            statusResponse.EnsureSuccessStatusCode();
            statusDoc = await JsonDocument.ParseAsync(await statusResponse.Content.ReadAsStreamAsync());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get status from {Host}", host);
            statusDoc = null;
        }

        var capabilities = new List<CapabilityDescriptor>();
        var metadata = new Dictionary<string, string> { ["host"] = host, ["generation"] = generation.ToString() };
        var nativeId = host;

        var shellyRoot = shellyDoc.RootElement;
        if (shellyRoot.TryGetProperty("mac", out var macProp))
            nativeId = macProp.GetString() ?? host;
        if (shellyRoot.TryGetProperty("type", out var typeProp))
            metadata["type"] = typeProp.GetString() ?? "";
        if (shellyRoot.TryGetProperty("model", out var modelProp))
            metadata["model"] = modelProp.GetString() ?? "";
        if (shellyRoot.TryGetProperty("fw", out var fwProp))
            metadata["firmware"] = fwProp.GetString() ?? "";

        // Read device name — Gen1: /settings, Gen2: /rpc/Shelly.GetDeviceInfo
        string? deviceName = null;
        try
        {
            var nameUrl = generation >= 2
                ? $"http://{host}/rpc/Shelly.GetDeviceInfo"
                : $"http://{host}/settings";
            var nameResponse = await http.GetAsync(nameUrl);
            nameResponse.EnsureSuccessStatusCode();
            var nameDoc = await JsonDocument.ParseAsync(await nameResponse.Content.ReadAsStreamAsync());
            if (nameDoc.RootElement.TryGetProperty("name", out var nameProp) &&
                nameProp.ValueKind == JsonValueKind.String)
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    deviceName = name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read device name from {Host}", host);
        }

        if (deviceName != null)
            metadata["name"] = deviceName;

        if (generation >= 2 && statusDoc != null)
        {
            var root = statusDoc.RootElement;
            if (root.TryGetProperty("switch:0", out _))
            {
                capabilities.Add(new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true });
                capabilities.Add(new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.Watts });
                capabilities.Add(new CapabilityDescriptor { Key = "energy", Label = "Energy", Unit = UnitType.KilowattHours });
            }
            if (root.TryGetProperty("pm1:0", out _) && capabilities.All(c => c.Key != "power"))
            {
                capabilities.Add(new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.Watts });
                capabilities.Add(new CapabilityDescriptor { Key = "energy", Label = "Energy", Unit = UnitType.KilowattHours });
            }
            if (root.TryGetProperty("cover:0", out _))
                capabilities.Add(new CapabilityDescriptor { Key = "cover", Label = "Cover", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 });
            if (root.TryGetProperty("temperature:0", out _))
                capabilities.Add(new CapabilityDescriptor { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius });
            if (root.TryGetProperty("humidity:0", out _))
                capabilities.Add(new CapabilityDescriptor { Key = "humidity", Label = "Humidity", Unit = UnitType.Percent });
        }
        else
        {
            // Gen1: detect from /shelly response and /status
            if (shellyRoot.TryGetProperty("num_rollers", out var rollers) && rollers.GetInt32() > 0)
                capabilities.Add(new CapabilityDescriptor { Key = "cover", Label = "Cover", Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 });
            if (shellyRoot.TryGetProperty("num_outputs", out var outputs) && outputs.GetInt32() > 0 && capabilities.All(c => c.Key != "cover"))
            {
                capabilities.Add(new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true });
                capabilities.Add(new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.Watts });
                capabilities.Add(new CapabilityDescriptor { Key = "energy", Label = "Energy", Unit = UnitType.KilowattHours });
            }
            if (shellyRoot.TryGetProperty("num_meters", out var meters) && meters.GetInt32() > 0 && capabilities.All(c => c.Key != "power"))
                capabilities.Add(new CapabilityDescriptor { Key = "power", Label = "Power", Unit = UnitType.Watts });

            if (statusDoc != null)
            {
                var root = statusDoc.RootElement;
                if (root.TryGetProperty("temperature", out _) || root.TryGetProperty("tmp", out _))
                    capabilities.Add(new CapabilityDescriptor { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius });
                if (root.TryGetProperty("hum", out _))
                    capabilities.Add(new CapabilityDescriptor { Key = "humidity", Label = "Humidity", Unit = UnitType.Percent });
            }
        }

        var allConfigured = await _deviceRepo.GetAllAsync();
        if (allConfigured.Any(d => d.NativeId == nativeId))
        {
            _logger.LogInformation("Shelly device {NativeId} at {Host} is already configured", nativeId, host);
            return Ok(new { status = "already_configured", host, nativeId, message = $"Device {nativeId} is already configured." });
        }

        var existing = await _discoveredRepo.GetByNativeIdAsync("shelly", nativeId);
        if (existing != null)
        {
            _logger.LogInformation("Shelly device {NativeId} at {Host} already discovered", nativeId, host);
            return Ok(new { status = "already_exists", host, nativeId, message = $"Device {nativeId} is already in the discovered list." });
        }

        var discovered = new DiscoveredDevice
        {
            Id = Guid.NewGuid(),
            CommunicationType = "shelly",
            NativeId = nativeId,
            Capabilities = capabilities,
            Metadata = metadata,
            DiscoveredAt = DateTime.UtcNow
        };
        await _discoveredRepo.UpsertAsync(discovered);

        _logger.LogInformation("Shelly device discovered: {NativeId} at {Host} with capabilities [{Caps}]",
            nativeId, host, string.Join(", ", capabilities.Select(c => c.Key)));

        return Ok(new { status = "discovered", host, nativeId, capabilities = capabilities.Select(c => c.Key).ToList() });
    }
}

public sealed record DiscoverShellyRequest(string Host);
