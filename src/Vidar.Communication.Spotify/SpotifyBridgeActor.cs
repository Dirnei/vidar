using System.Net.Http;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Webhooks;

namespace Vidar.Communication.Spotify;

// Bridge for the Spotify integration. Owns OAuth (reusing the host OAuthController + WebhookRegistry
// relay, exactly like HomeConnect), discovers the single "Spotify Player" device, and spawns one
// SpotifyPlayerActor that owns the live polling connection.
public sealed class SpotifyBridgeActor : PluginActorBase
{
    protected override string PluginId => "spotify";

    private const string PlayerNativeId = "player";

    private readonly string _tokenFilePath;
    private readonly IActorRef _webhookRegistry;

    private SpotifyOAuth? _oauth;
    private HttpClient? _http;
    private IActorRef? _player;

    private sealed record TokensReady();
    private sealed record AwaitingAuth();
    private sealed record ExchangeFailed(string Error);

    public static Props Props(string tokenFilePath) =>
        Akka.Actor.Props.Create(() => new SpotifyBridgeActor(tokenFilePath));

    public SpotifyBridgeActor(string tokenFilePath)
    {
        _tokenFilePath = tokenFilePath;
        _webhookRegistry = ActorRegistry.For(Context.System).Get<WebhookRegistry>();

        Receive<DeviceCommand>(cmd =>
        {
            if (_player is not null) _player.Forward(cmd);
            else Log.Warning("Spotify command dropped — no player actor yet");
        });

        Receive<OAuthCallbackReceived>(OnOAuthCallback);
        Receive<TokensReady>(_ => { PublishStatus("running", ConfiguredDeviceCount); DiscoverPlayer(); });
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
        _webhookRegistry.Tell(new RegisterWebhookListener("oauth-spotify", Self, IntegrationId: "spotify"));
    }

    protected override void PostStop()
    {
        _http?.Dispose();
        base.PostStop();
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        if (enabled) StartOAuth(settings);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (enabled) StartOAuth(settings);
        else
        {
            if (_player is not null) { Context.Stop(_player); _player = null; }
            PublishStatus("stopped", 0);
        }
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        if (nativeId != PlayerNativeId || _oauth is null) return;
        SpawnPlayer(deviceId);
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
        var hostBaseUrl = settings.GetValueOrDefault("hostBaseUrl", "http://vidar-host:8080");
        var redirectUri = $"{hostBaseUrl.TrimEnd('/')}/api/oauth/spotify/callback";

        _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var store = new SpotifyTokenStore(_tokenFilePath);
        _oauth = new SpotifyOAuth(_http, store, clientId, clientSecret, redirectUri, tokenEndpoint);

        // If we already hold a usable token, go straight to discovery; else wait for the callback.
        _ = TryExistingTokenAsync();
    }

    private async Task TryExistingTokenAsync()
    {
        try
        {
            var token = await _oauth!.GetAccessTokenAsync(CancellationToken.None);
            if (token is not null) Self.Tell(new TokensReady());
            else
            {
                Log.Info("No Spotify token yet — awaiting OAuth via /api/oauth/spotify/authorize");
                Self.Tell(new AwaitingAuth());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Spotify existing-token check failed");
            Self.Tell(new ExchangeFailed(ex.Message));
        }
    }

    private void OnOAuthCallback(OAuthCallbackReceived msg)
    {
        if (_oauth is null) { Log.Warning("Spotify OAuth callback but not configured"); return; }
        _ = ExchangeAsync(msg.Code);
    }

    private async Task ExchangeAsync(string code)
    {
        try
        {
            await _oauth!.ExchangeCodeAsync(code, CancellationToken.None);
            var token = await _oauth.GetAccessTokenAsync(CancellationToken.None);
            if (token is null) { Self.Tell(new ExchangeFailed("token exchange returned no access token")); return; }
            Self.Tell(new TokensReady());
        }
        catch (Exception ex) { Self.Tell(new ExchangeFailed(ex.Message)); }
    }

    private void DiscoverPlayer()
    {
        var deviceId = GetDeviceId(PlayerNativeId) ?? Guid.NewGuid();
        Discover(deviceId, PlayerNativeId, SpotifyCapabilities.Build(), new Dictionary<string, string>
        {
            ["name"] = "Spotify Player",
        });
        Log.Info("Discovered Spotify Player device");
        if (GetDeviceId(PlayerNativeId) is Guid existing) SpawnPlayer(existing);
    }

    private void SpawnPlayer(Guid deviceId)
    {
        if (_oauth is null) return;
        if (_player is not null) { Context.Stop(_player); _player = null; }
        _player = Context.ActorOf(SpotifyPlayerActor.Props(deviceId, _oauth), $"spotify-player-{Guid.NewGuid():N}");
        Log.Info("Spawned SpotifyPlayerActor for {DeviceId}", deviceId);
        PublishStatus("running", 1);
    }
}
