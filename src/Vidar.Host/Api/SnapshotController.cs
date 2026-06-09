using Microsoft.AspNetCore.Mvc;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/devices")]
public sealed class SnapshotController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly IApplicationConfigRepository _appConfigRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SnapshotController> _logger;

    public SnapshotController(
        IDeviceRepository deviceRepo,
        IApplicationConfigRepository appConfigRepo,
        IHttpClientFactory httpClientFactory,
        ILogger<SnapshotController> logger)
    {
        _deviceRepo = deviceRepo;
        _appConfigRepo = appConfigRepo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("{id:guid}/snapshot")]
    public async Task<IActionResult> GetSnapshot(Guid id)
    {
        var device = await _deviceRepo.GetByIdAsync(id);
        if (device == null) return NotFound();

        if (device.CommunicationType != "unifi" || !device.NativeId.StartsWith("protect-"))
            return BadRequest("Device does not support snapshots");

        var config = await _appConfigRepo.GetByIdAsync("unifi");
        if (config == null || !config.Enabled)
            return StatusCode(503, "UniFi application is not configured");

        if (!config.Settings.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
            return StatusCode(503, "UniFi host not configured");

        config.Settings.TryGetValue("apiKey", out var apiKey);

        var protectMac = device.NativeId["protect-".Length..];

        var http = _httpClientFactory.CreateClient("protect");
        http.DefaultRequestHeaders.Remove("X-API-Key");
        http.DefaultRequestHeaders.Add("X-API-Key", apiKey ?? "");

        var baseUrl = $"https://{host}/proxy/protect/integration/v1";

        // Find camera ID by MAC
        var listResponse = await http.GetAsync($"{baseUrl}/cameras");
        if (!listResponse.IsSuccessStatusCode)
            return StatusCode(502, "Failed to reach Protect API");

        var listJson = await listResponse.Content.ReadAsStringAsync();
        var cameras = System.Text.Json.JsonSerializer.Deserialize<CameraListResponse>(listJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var camera = cameras?.Data?.FirstOrDefault(c =>
            string.Equals(c.MacAddress, protectMac, StringComparison.OrdinalIgnoreCase));

        if (camera == null)
            return NotFound("Camera not found on Protect controller");

        // Fetch snapshot (POST, not GET)
        using var snapshotRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/cameras/{camera.Id}/snapshot");
        var snapshotResponse = await http.SendAsync(snapshotRequest);
        if (!snapshotResponse.IsSuccessStatusCode)
            return StatusCode(502, "Failed to fetch snapshot");

        var bytes = await snapshotResponse.Content.ReadAsByteArrayAsync();
        return File(bytes, "image/jpeg");
    }

    private sealed record CameraListResponse(List<CameraEntry>? Data);
    private sealed record CameraEntry(string Id, string? MacAddress);
}
