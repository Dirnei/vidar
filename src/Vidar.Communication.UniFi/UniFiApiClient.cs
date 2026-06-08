using System.Net.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vidar.Communication.UniFi;

public sealed class UniFiApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public UniFiApiClient(string host)
    {
        _baseUrl = $"https://{host}/proxy/network/integration/v1";

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

    public async Task<List<UniFiSite>> GetSitesAsync(CancellationToken ct = default)
    {
        return await FetchAllPagesAsync<UniFiSite>("/sites", ct);
    }

    public async Task<List<UniFiNetworkDevice>> GetDevicesAsync(string siteId, CancellationToken ct = default)
    {
        return await FetchAllPagesAsync<UniFiNetworkDevice>($"/sites/{siteId}/devices", ct);
    }

    public async Task<UniFiDeviceStats?> GetDeviceStatsAsync(string siteId, string deviceId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/sites/{siteId}/devices/{deviceId}/statistics/latest";
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<UniFiDeviceStats>(json, JsonOptions);
    }

    public async Task<List<UniFiClient>> GetClientsAsync(string siteId, CancellationToken ct = default)
    {
        return await FetchAllPagesAsync<UniFiClient>($"/sites/{siteId}/clients", ct);
    }

    public async Task<bool> PowerCyclePortAsync(string siteId, string deviceId, int portIdx, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/sites/{siteId}/devices/{deviceId}/interfaces/ports/{portIdx}/actions";
        var body = JsonSerializer.Serialize(new { action = "power-cycle" });
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<List<T>> FetchAllPagesAsync<T>(string path, CancellationToken ct)
    {
        var result = new List<T>();
        var offset = 0;
        const int limit = 200;

        while (true)
        {
            var url = $"{_baseUrl}{path}?offset={offset}&limit={limit}";
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var page = JsonSerializer.Deserialize<PagedResponse<T>>(json, JsonOptions);
            if (page == null || page.Data.Count == 0) break;

            result.AddRange(page.Data);
            offset += page.Data.Count;
            if (offset >= page.TotalCount) break;
        }

        return result;
    }

    public void Dispose() => _http.Dispose();
}
