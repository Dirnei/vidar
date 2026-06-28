using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Vidar.Communication.Dyson;

public sealed record DysonIoTCredentials(
    string Endpoint, string ClientId, string TokenKey, string TokenValue,
    string TokenSignature, string CustomAuthorizerName);

public sealed class DysonAuthExpiredException(string serial)
    : Exception($"Dyson account token expired for {serial}");

public sealed class DysonCloudIot
{
    private readonly HttpClient _http;

    public DysonCloudIot(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://appapi.cp.dyson.com");
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("android client"))
            _http.DefaultRequestHeaders.Add("User-Agent", "android client");
    }

    public async Task<DysonIoTCredentials> GetCredentialsAsync(string serial, string accountToken, CancellationToken ct)
    {
        // NOTE: the Dyson API requires the body field to be PascalCase "Serial".
        // System.Net.Http.Json's JsonContent.Create uses web defaults (camelCase) which yields
        // {"serial":...} and gets rejected with 400. JsonSerializer.Serialize keeps PascalCase.
        var req = new HttpRequestMessage(HttpMethod.Post, "/v2/authorize/iot-credentials")
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { Serial = serial }),
                System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accountToken);

        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new DysonAuthExpiredException(serial);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<IotResponse>(ct)
            ?? throw new InvalidOperationException("Empty iot-credentials response");
        var c = body.IoTCredentials;
        return new DysonIoTCredentials(body.Endpoint, c.ClientId, c.TokenKey, c.TokenValue, c.TokenSignature, c.CustomAuthorizerName);
    }

    private sealed record IotResponse(string Endpoint, IotCreds IoTCredentials);
    private sealed record IotCreds(string ClientId, string TokenValue, string TokenKey, string TokenSignature, string CustomAuthorizerName);
}
