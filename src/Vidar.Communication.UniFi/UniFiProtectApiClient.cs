using System.Net.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vidar.Communication.UniFi;

public sealed class UniFiProtectApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public UniFiProtectApiClient(string host)
    {
        _baseUrl = $"https://{host}/proxy/protect/integration/v1";

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void SetApiKey(string apiKey)
    {
        _http.DefaultRequestHeaders.Remove("X-API-Key");
        _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public async Task<List<UniFiCamera>> GetCamerasAsync(CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/cameras";
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<UniFiCamera>>(json, JsonOptions) ?? [];
    }

    public async Task<List<UniFiRtspStream>> GetRtspStreamsAsync(string cameraId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/cameras/{cameraId}/rtsps-stream";
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<UniFiRtspStream>>(json, JsonOptions) ?? [];
    }

    public async Task<byte[]?> GetSnapshotAsync(string cameraId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/cameras/{cameraId}/snapshot";
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public void Dispose() => _http.Dispose();
}
