using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Vidar.Host.Dreo;

// NOTE: sidecar /auth endpoint + live verification deferred to E2E
public sealed class SidecarDreoAuth : IDreoAuth
{
    private readonly HttpClient _http;
    public SidecarDreoAuth(HttpClient http) => _http = http;

    public async Task<DreoAuthResult> PasswordLoginAsync(string email, string password, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("/auth/login", new { email, password }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SidecarAuthResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Dreo onboarding sidecar returned an empty response.");
        return new DreoAuthResult(body.UserDataJson, body.Region, body.Devices
            .Select(d => new DreoManifestEntry(d.Serial, d.Model, d.Name)).ToList());
    }

    private sealed class SidecarAuthResponse
    {
        [JsonPropertyName("userDataJson")] public string UserDataJson { get; set; } = "";
        [JsonPropertyName("region")] public string Region { get; set; } = "";
        [JsonPropertyName("devices")] public List<SidecarDevice> Devices { get; set; } = new();
    }

    private sealed class SidecarDevice
    {
        [JsonPropertyName("serial")] public string Serial { get; set; } = "";
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }
}
