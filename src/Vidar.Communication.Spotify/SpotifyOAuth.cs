using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Owns the Spotify Authorization Code token lifecycle: exchange the callback code, then keep a
// fresh access token (refresh within 60s of expiry). Basic-auth per Spotify's confidential
// client flow. Persists through SpotifyTokenStore so restarts don't force re-auth.
public sealed class SpotifyOAuth
{
    private readonly HttpClient _http;
    private readonly SpotifyTokenStore _store;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly string _tokenEndpoint;

    public SpotifyOAuth(HttpClient http, SpotifyTokenStore store, string clientId, string clientSecret,
        string redirectUri, string tokenEndpoint)
    {
        _http = http;
        _store = store;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _redirectUri = redirectUri;
        _tokenEndpoint = tokenEndpoint;
    }

    public async Task ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUri,
        };
        var token = await PostTokenAsync(form, previousRefresh: null, ct);
        if (token is not null) await _store.SaveAsync(token);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var current = await _store.LoadAsync();
        if (current is null) return null;

        if (DateTimeOffset.UtcNow < current.ExpiresAt - TimeSpan.FromSeconds(60))
            return current.AccessToken;

        if (string.IsNullOrEmpty(current.RefreshToken)) return null;

        var refreshed = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
        }, previousRefresh: current.RefreshToken, ct);

        if (refreshed is null) return null;
        await _store.SaveAsync(refreshed);
        return refreshed.AccessToken;
    }

    private async Task<SpotifyToken?> PostTokenAsync(Dictionary<string, string> form,
        string? previousRefresh, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return null;

        var json = JsonDocument.Parse(body).RootElement;
        var access = json.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(access)) return null;
        var refresh = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : previousRefresh;
        var expiresIn = json.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s) ? s : 3600;

        return new SpotifyToken(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }
}
