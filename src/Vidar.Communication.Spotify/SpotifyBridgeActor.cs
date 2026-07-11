using System.Net;
using System.Net.Http;
using System.Text;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Webhooks;

namespace Vidar.Communication.Spotify;

// Bridge for the Spotify integration. Owns OAuth (reusing the host OAuthController + WebhookRegistry
// relay, exactly like HomeConnect) AND the live Spotify Web API connection: on each fetch it pulls
// /me/player + /me/player/devices, discovers a Vidar device per Spotify Connect speaker, fans the
// single global playback state out to every accepted twin, and persists per-device volumes. Spotify's
// Web API is pull-only, so fetches are on-demand (client-driven refresh commands + a fetch after each
// write), never on a background timer.
public sealed class SpotifyBridgeActor : PluginActorBase
{
    protected override string PluginId => "spotify";

    private const string ApiBase = "https://api.spotify.com/v1";

    private readonly string _tokenFilePath;
    private readonly SpotifyVolumeStore _volumeStore;
    private readonly IActorRef _webhookRegistry;

    private SpotifyOAuth? _oauth;
    private HttpClient? _http;

    // Accepted Spotify speakers: nativeId (Spotify device id) -> Vidar device id. Populated from
    // OnDeviceRegistered and consulted for state fan-out.
    private readonly Dictionary<string, Guid> _accepted = new();
    // Last-known per-device volumes, loaded from the store in PreStart and kept in sync on fetch/command.
    private Dictionary<string, int> _volumes = new();
    // The device id Spotify reports as active in the last fetch — drives play-vs-transfer command shaping.
    private string? _activeDeviceId;

    private sealed record TokensReady();
    private sealed record AwaitingAuth();
    private sealed record ExchangeFailed(string Error);
    private sealed class RefreshNow { public static readonly RefreshNow Instance = new(); }
    private sealed record VolumesLoaded(Dictionary<string, int> Volumes);
    private sealed record DevicesFetched(SpotifyPlayback Playback, IReadOnlyList<SpotifyDevice> Devices);
    private sealed record VolumeChanged(string NativeId, int Volume);

    public static Props Props(string tokenFilePath, string volumeFilePath) =>
        Akka.Actor.Props.Create(() => new SpotifyBridgeActor(tokenFilePath, volumeFilePath));

    public SpotifyBridgeActor(string tokenFilePath, string volumeFilePath)
    {
        _tokenFilePath = tokenFilePath;
        _volumeStore = new SpotifyVolumeStore(volumeFilePath);
        _webhookRegistry = ActorRegistry.For(Context.System).Get<WebhookRegistry>();

        Receive<DeviceCommand>(cmd => { _ = HandleCommandAsync(cmd); });
        Receive<RefreshNow>(msg => { _ = FetchAllAsync(); });
        Receive<VolumesLoaded>(m => _volumes = m.Volumes);
        Receive<DevicesFetched>(OnDevicesFetched);
        Receive<VolumeChanged>(m => { _volumes[m.NativeId] = m.Volume; _ = PersistVolumesAsync(); });

        Receive<OAuthCallbackReceived>(OnOAuthCallback);
        // The webhook-registry singleton lives on the host; when it (re)starts our registration is
        // lost, so re-register on its startup announcement or OAuth callbacks get dropped.
        Receive<WebhookRegistryStarted>(_ => RegisterOAuthListener());
        // OAuth is ready: announce running and kick off a single fetch (discovery happens from the
        // fetch, per speaker). No periodic timer — Spotify's API is pull-only.
        Receive<TokensReady>(_ => { PublishStatus("running", ConfiguredDeviceCount); Self.Tell(RefreshNow.Instance); });
        Receive<AwaitingAuth>(_ => PublishStatus("awaiting_auth", 0));
        Receive<ExchangeFailed>(m =>
        {
            Log.Error("Spotify token exchange failed: {Error}", m.Error);
            PublishStatus("error", 0, m.Error);
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        Mediator.Tell(new Subscribe("webhook-registry-started", Self));
        RegisterOAuthListener();
        // Load persisted volumes off the actor thread; VolumesLoaded copies them in on receipt.
        _volumeStore.LoadAsync().PipeTo(Self, success: v => new VolumesLoaded(v));
    }

    private void RegisterOAuthListener() =>
        _webhookRegistry.Tell(new RegisterWebhookListener("oauth-spotify", Self, IntegrationId: "spotify"));

    protected override void PostStop()
    {
        _http?.Dispose();
        base.PostStop();
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        if (enabled) StartOAuth(settings);
        // Announce presence even when disabled so the application appears in the UI before it is
        // configured (the host lists an application only once it publishes a status heartbeat).
        else PublishStatus("stopped", 0);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (enabled) StartOAuth(settings);
        else PublishStatus("stopped", 0);
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        // Every accepted speaker is keyed by its Spotify device id (nativeId) for fan-out.
        _accepted[nativeId] = deviceId;
    }

    private void StartOAuth(Dictionary<string, string> settings)
    {
        var clientId = settings.GetValueOrDefault("clientId", "");
        var clientSecret = settings.GetValueOrDefault("clientSecret", "");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Log.Warning("Spotify clientId/clientSecret not configured — staying idle");
            PublishStatus("awaiting_auth", 0);
            return;
        }

        var tokenEndpoint = settings.GetValueOrDefault("oauthTokenEndpoint", "https://accounts.spotify.com/api/token");

        _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var store = new SpotifyTokenStore(_tokenFilePath);
        _oauth = new SpotifyOAuth(_http, store, clientId, clientSecret, tokenEndpoint);

        // If we already hold a usable token, go straight to running/fetch; else wait for the callback.
        _ = TryExistingTokenAsync();
    }

    private async Task TryExistingTokenAsync()
    {
        // Self/Context/Sender are only valid on the actor thread; capture Self before awaiting so the
        // post-await continuation (which runs off the actor thread) can still message this actor.
        var self = Self;
        try
        {
            var token = await _oauth!.GetAccessTokenAsync(CancellationToken.None);
            if (token is not null) self.Tell(new TokensReady());
            else
            {
                Log.Info("No Spotify token yet — awaiting OAuth via /api/oauth/spotify/authorize");
                self.Tell(new AwaitingAuth());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Spotify existing-token check failed");
            self.Tell(new ExchangeFailed(ex.Message));
        }
    }

    private void OnOAuthCallback(OAuthCallbackReceived msg)
    {
        if (_oauth is null) { Log.Warning("Spotify OAuth callback but not configured"); return; }
        _ = ExchangeAsync(msg.Code, msg.RedirectUri);
    }

    private async Task ExchangeAsync(string code, string redirectUri)
    {
        // Capture Self before awaiting — it is not accessible from the post-await continuation.
        var self = Self;
        try
        {
            await _oauth!.ExchangeCodeAsync(code, redirectUri, CancellationToken.None);
            var token = await _oauth.GetAccessTokenAsync(CancellationToken.None);
            if (token is null) { self.Tell(new ExchangeFailed("token exchange returned no access token")); return; }
            self.Tell(new TokensReady());
        }
        catch (Exception ex) { self.Tell(new ExchangeFailed(ex.Message)); }
    }

    // --- command handling ------------------------------------------------------------------------

    private async Task HandleCommandAsync(DeviceCommand cmd)
    {
        if (!IsEnabled) return;

        // Capture Self before the first await — the post-await continuation runs off the actor thread.
        var self = Self;

        // Refresh is an explicit on-demand fetch (the frontend's viewing-gated tick + refresh button),
        // not a Spotify write — trigger a one-shot GET rather than the command builder.
        if (cmd.CapabilityKey is "refresh" or "refresh_zones") { self.Tell(RefreshNow.Instance); return; }
        if (_oauth is null || _http is null) { Log.Warning("Spotify command dropped — not authorized"); return; }

        var isActive = _activeDeviceId == cmd.NativeId;
        var spec = SpotifyCommandBuilder.Build(cmd.CapabilityKey, cmd.Value, cmd.NativeId, isActive);
        if (spec is null) { Log.Warning("Unrecognized Spotify command {Key}={Value}", cmd.CapabilityKey, cmd.Value); return; }

        var token = await _oauth.GetAccessTokenAsync(CancellationToken.None);
        if (token is null) { Log.Warning("Spotify command dropped — no access token"); return; }

        await SendAsync(spec, token, cmd.CapabilityKey);

        // Persist the user's chosen volume only after the API write so a persist hiccup (e.g. an
        // overlapping save from a slider drag throwing IOException) can never swallow the actual
        // Spotify volume change — it can at worst leave the twin's cached volume briefly stale.
        // The mutation + persist are marshalled back onto the actor thread via a self-message: this
        // continuation runs off the actor thread, and _volumes is a plain Dictionary shared with
        // OnDevicesFetched (which runs on the actor thread), so touching it here would race.
        if (cmd.CapabilityKey == "volume")
            self.Tell(new VolumeChanged(cmd.NativeId, ToVolume(cmd.Value)));

        self.Tell(RefreshNow.Instance); // reflect the change quickly
    }

    private async Task FetchAllAsync()
    {
        if (!IsEnabled) return;

        var self = Self;
        try
        {
            if (_oauth is null || _http is null) return;
            var token = await _oauth.GetAccessTokenAsync(CancellationToken.None);
            if (token is null) return;

            var playerJson = await GetAsync("/me/player", token) ?? "";
            var devicesJson = await GetAsync("/me/player/devices", token) ?? "";
            var playback = SpotifyStateMapper.Parse(playerJson);
            var devices = SpotifyDeviceListMapper.Parse(devicesJson);
            self.Tell(new DevicesFetched(playback, devices)); // back on the actor thread for state mutation
        }
        catch (Exception ex) { Log.Warning(ex, "Spotify fetch failed"); }
    }

    // Runs on the actor thread (via the DevicesFetched self-message) — safe to touch state,
    // Discover, and ReportState here.
    private void OnDevicesFetched(DevicesFetched m)
    {
        _activeDeviceId = m.Playback.ActiveDeviceId;

        // 1) Discover any speaker we don't yet track. Id/name reconciliation is a v1 no-op:
        //    RegisterDeviceForPolling doesn't carry the accepted device's friendly name, so we can't
        //    cheaply detect an id rotation. A rotated Spotify device id therefore creates a duplicate
        //    twin the user can delete (acceptable v1; full reconciliation is a follow-up).
        foreach (var d in m.Devices)
        {
            if (GetDeviceId(d.Id) is not null) continue;             // already accepted by id
            if (_accepted.Count > 0 && AcceptedNameMatches(d.Name)) continue; // reconcile-by-name guard
            Discover(Guid.NewGuid(), d.Id, SpotifyCapabilities.Build(),
                new Dictionary<string, string> { ["name"] = d.Name });
        }

        // 2) Persist any live volumes that changed.
        var changed = false;
        foreach (var d in m.Devices)
            if (d.VolumePercent is int v && (!_volumes.TryGetValue(d.Id, out var cur) || cur != v))
            {
                _volumes[d.Id] = v;
                changed = true;
            }
        if (changed) _ = PersistVolumesAsync();

        // 3) Fan the single global playback state out to every accepted twin.
        foreach (var (deviceId, updates) in SpotifyFanOut.Build(m.Playback, m.Devices, _accepted, _volumes))
            foreach (var (key, value) in updates)
                ReportState(deviceId, key, value);
    }

    // v1: no cheap access to the accepted device's friendly name (RegisterDeviceForPolling carries
    // only the native id), so this guard is a no-op. See the id-rotation note in OnDevicesFetched.
    private bool AcceptedNameMatches(string name) => false;

    // Persists _volumes, swallowing failures (e.g. two overlapping saves — a slider drag can fire a
    // command save and a fetch-driven save close together — racing on the underlying file and
    // throwing IOException). A lost persist just means the cached volume is briefly stale; it must
    // never fault the caller's fire-and-forget task or abort a command handler mid-flight.
    private async Task PersistVolumesAsync()
    {
        try { await _volumeStore.SaveAsync(_volumes); }
        catch (Exception ex) { Log.Warning(ex, "Spotify volume persist failed"); }
    }

    // --- HTTP -----------------------------------------------------------------------------------

    // Returns the response body ("" for 204), or null when the call could not be made/authorized.
    private async Task<string?> GetAsync(string path, string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http!.SendAsync(req, CancellationToken.None);
            if (resp.StatusCode == HttpStatusCode.NoContent) return "";
            if (resp.StatusCode == (HttpStatusCode)429) { Log.Debug("Spotify rate-limited on {Path}", path); return null; }
            if (!resp.IsSuccessStatusCode) { Log.Warning("Spotify GET {Path} → {Code}", path, (int)resp.StatusCode); return null; }
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { Log.Warning(ex, "Spotify GET {Path} failed", path); return null; }
    }

    private async Task SendAsync(SpotifyRequest spec, string token, string capabilityKey)
    {
        var url = ApiBase + spec.Path;
        var query = string.Join("&", spec.Query.Where(kv => kv.Value is not null)
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        if (query.Length > 0) url += "?" + query;

        using var req = new HttpRequestMessage(spec.Method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (spec.JsonBody is not null)
            req.Content = new StringContent(spec.JsonBody, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http!.SendAsync(req, CancellationToken.None);
            if (resp.StatusCode == HttpStatusCode.Forbidden)
                Log.Warning("Spotify command {Key} forbidden (403) — Spotify Premium is required", capabilityKey);
            else if (!resp.IsSuccessStatusCode)
                Log.Warning("Spotify command {Key} → {Code}", capabilityKey, (int)resp.StatusCode);
        }
        catch (Exception ex) { Log.Warning(ex, "Spotify command {Key} failed", capabilityKey); }
    }

    private static int ToVolume(object v) => Math.Clamp(v switch
    {
        double d => (int)Math.Round(d),
        int i => i,
        long l => (int)l,
        string s when double.TryParse(s, out var d) => (int)Math.Round(d),
        _ => 0,
    }, 0, 100);
}
