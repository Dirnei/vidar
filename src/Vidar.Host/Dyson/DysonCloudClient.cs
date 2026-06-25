using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vidar.Host.Dyson;

public sealed partial class DysonCloudClient
{
    private readonly HttpClient _http;

    public DysonCloudClient(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("android client"))
            _http.DefaultRequestHeaders.Add("User-Agent", "android client");
    }

    public async Task<string> BeginLoginAsync(string region, string email, CancellationToken ct)
    {
        // Optional pre-step (ignore failures): user status
        await _http.PostAsJsonAsync(
            $"/v3/userregistration/email/userstatus?country={region}", new { email }, ct);

        var resp = await _http.PostAsJsonAsync(
            $"/v3/userregistration/email/auth?country={region}&culture=en-US", new { email }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AuthChallenge>(ct);
        return body!.ChallengeId;
    }

    public async Task<string> VerifyLoginAsync(string region, string email, string password,
        string challengeId, string otp, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/v3/userregistration/email/verify",
            new { email, password, challengeId, otpCode = otp }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AuthToken>(ct);
        return body!.Token;
    }

    public async Task<IReadOnlyList<DysonDevice>> GetDevicesAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/v2/provisioningservice/manifest");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadFromJsonAsync<List<ManifestDevice>>(ct) ?? new();

        return raw.Select(d => new DysonDevice(
            Serial: d.Serial,
            ProductType: d.ProductType,
            Name: d.Name,
            MqttPassword: DecryptLocalCredentials(d.LocalCredentials),
            Variant: d.Variant)).ToList();
    }

    public static string DecryptLocalCredentials(string encryptedBase64)
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var iv = new byte[16];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var cipher = Convert.FromBase64String(encryptedBase64);
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        var json = Encoding.UTF8.GetString(plain);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("apPasswordHash").GetString()
            ?? throw new InvalidOperationException("apPasswordHash missing in decrypted credentials");
    }

    private sealed record AuthChallenge(string ChallengeId);
    private sealed record AuthToken(string Token, string TokenType);
    private sealed record ManifestDevice(string Serial, string ProductType, string Name,
        string LocalCredentials, string? Variant);
}
