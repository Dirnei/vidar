using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Vidar.Host.Loxone;

// NOTE: sidecar /auth/login endpoint validates one Miniserver at a time; live verification
// against a real Miniserver is deferred to E2E (mirrors SidecarDreoAuth).
public sealed class LoxoneSidecar : ILoxoneSidecar
{
    private readonly HttpClient _http;
    public LoxoneSidecar(HttpClient http) => _http = http;

    public async Task<LoxoneProbeResult> ProbeAsync(string host, string user, string password, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/auth/login", new { host, user, password }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SidecarProbeResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Loxone onboarding sidecar returned an empty response.");
        return new LoxoneProbeResult(body.Serial, body.ControlCount, body.RoomCount);
    }

    private sealed class SidecarProbeResponse
    {
        [JsonPropertyName("serial")] public string Serial { get; set; } = "";
        [JsonPropertyName("controlCount")] public int ControlCount { get; set; }
        [JsonPropertyName("roomCount")] public int RoomCount { get; set; }
    }
}
