using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Vidar.Host.Roborock;

// NOTE: sidecar /auth endpoint + live verification deferred to E2E
public sealed class SidecarRoborockAuth : IRoborockAuth
{
    private readonly HttpClient _http;

    public SidecarRoborockAuth(HttpClient http) => _http = http;

    public async Task<RoborockAuthResult> PasswordLoginAsync(string email, string password, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/auth/login", new { email, password }, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SidecarAuthResponse>(cancellationToken: ct);
        return ToAuthResult(result!);
    }

    public async Task RequestCodeAsync(string email, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/auth/request-code", new { email }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<RoborockAuthResult> CodeLoginAsync(string email, string code, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/auth/code-login", new { email, code }, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SidecarAuthResponse>(cancellationToken: ct);
        return ToAuthResult(result!);
    }

    private static RoborockAuthResult ToAuthResult(SidecarAuthResponse r) =>
        new(r.UserDataJson, r.Devices
            .Select(d => new RoborockManifestEntry(d.Duid, d.Name, d.Model, d.LocalKey, d.Ip))
            .ToList());

    private sealed class SidecarAuthResponse
    {
        [JsonPropertyName("userDataJson")]
        public string UserDataJson { get; set; } = "";

        [JsonPropertyName("devices")]
        public List<SidecarDevice> Devices { get; set; } = new();
    }

    private sealed class SidecarDevice
    {
        [JsonPropertyName("duid")] public string Duid { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("localKey")] public string LocalKey { get; set; } = "";
        [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    }
}
