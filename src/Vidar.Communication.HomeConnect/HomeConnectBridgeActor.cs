using System.Net.Http.Json;
using System.Text.Json;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHomeConnect;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Commands;
using TurboHomeConnect.Model;
using TurboHomeConnect.OAuth;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Webhooks;

namespace Vidar.Communication.HomeConnect;

public sealed class HomeConnectBridgeActor : PluginActorBase
{
    protected override string PluginId => "homeconnect";

    private readonly IActorRef _webhookRegistry;

    private readonly Dictionary<string, Guid> _haIdToDeviceId = new();
    private readonly Dictionary<Guid, string> _deviceIdToHaId = new();

    private IHomeConnectClient? _client;
    private HomeConnectAuthorizationCodeFlow? _oauthFlow;
    private IMaterializer? _materializer;
    private bool _sseStarted;

    private sealed class StreamInit { public static readonly StreamInit Instance = new(); }
    private sealed class StreamAck { public static readonly StreamAck Instance = new(); }
    private sealed class StreamComplete { public static readonly StreamComplete Instance = new(); }
    private sealed record StreamFailed(Exception Cause);
    private sealed class BeginSseStream { public static readonly BeginSseStream Instance = new(); }
    private sealed record TokenExchangeFailed(string Error);

    public static Props Props() =>
        Akka.Actor.Props.Create(() => new HomeConnectBridgeActor());

    public HomeConnectBridgeActor()
    {
        _webhookRegistry = ActorRegistry.For(Context.System).Get<WebhookRegistry>();

        Receive<DeviceCommand>(OnDeviceCommand);
        Receive<WebhookReceived>(OnWebhookReceived);
        Receive<OAuthCallbackReceived>(OnOAuthCallback);
        // The webhook-registry singleton lives on the host; when it (re)starts our registration is
        // lost, so re-register on its startup announcement or OAuth callbacks get dropped.
        Receive<WebhookRegistryStarted>(_ => RegisterOAuthListener());
        Receive<BeginSseStream>(_ => StartSseStream());
        Receive<TokenExchangeFailed>(msg =>
        {
            Log.Error("OAuth token exchange failed: {Error}", msg.Error);
            PublishStatus("error", 0, $"OAuth token exchange failed: {msg.Error}");
        });

        // Akka.Streams flow messages
        Receive<StreamInit>(_ => Sender.Tell(StreamAck.Instance));
        Receive<IHomeConnectMessage>(OnHomeConnectMessage);
        Receive<StreamComplete>(_ => Log.Info("Home Connect event stream completed"));
        Receive<StreamFailed>(msg => Log.Error(msg.Cause, "Home Connect event stream failed"));
    }

    protected override void PreStart()
    {
        base.PreStart();
        Mediator.Tell(new Subscribe("webhook-registry-started", Self));
        RegisterOAuthListener();
    }

    private void RegisterOAuthListener() =>
        _webhookRegistry.Tell(new RegisterWebhookListener(
            "oauth-homeconnect", Self, IntegrationId: "homeconnect"));

    protected override void PostStop()
    {
        DisposeClient();
        base.PostStop();
    }

    protected override void OnPluginRegistered(bool enabled, Dictionary<string, string> settings,
        List<RegisterDeviceForPolling> registrations)
    {
        Log.Info("Plugin registered with {Count} devices, enabled={Enabled}", registrations.Count, enabled);

        if (enabled)
            StartClient(settings);
    }

    protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
    {
        if (enabled)
            StartClient(settings);
        else
            DisposeClient();
    }

    protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
    {
        _haIdToDeviceId[nativeId] = deviceId;
        _deviceIdToHaId[deviceId] = nativeId;
        Log.Info("Registered device {DeviceId} → {HaId}", deviceId, nativeId);
    }

    private void StartClient(Dictionary<string, string> settings)
    {
        DisposeClient();

        var clientId = settings.GetValueOrDefault("clientId", "");
        var clientSecret = settings.GetValueOrDefault("clientSecret");
        var useSimulator = settings.GetValueOrDefault("useSimulator", "false") == "true";
        var tokenFilePath = settings.GetValueOrDefault("tokenFilePath", "/data/homeconnect-token.json");

        if (string.IsNullOrEmpty(clientId))
        {
            Log.Warning("Home Connect clientId not configured — staying idle");
            return;
        }

        // The options object requires a redirect URI, but it is never sent on the wire here: the
        // authorization-code exchange is done manually in ExchangeCodeForTokensAsync using the
        // host-resolved redirect URI, and the flow is used only as a refresh token provider (refresh
        // does not send redirect_uri). This placeholder just satisfies the options contract.
        var redirectUri = new Uri("http://vidar-host:8080/api/oauth/homeconnect/callback");

        var oauthOptions = useSimulator
            ? HomeConnectOAuthOptions.ForSimulator(clientId, redirectUri, clientSecret)
            : new HomeConnectOAuthOptions
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                RedirectUri = redirectUri,
                TokenStore = new FileTokenStore(tokenFilePath),
                OpenBrowser = false,
            };

        _oauthFlow = new HomeConnectAuthorizationCodeFlow(oauthOptions);

        var builder = HomeConnectBuilder.Create();
        if (useSimulator)
            builder.UseSimulator();
        else
            builder.UseProduction();

        _client = builder
            .TokenProvider(_oauthFlow.GetAccessTokenAsync)
            .Build(Context.System);

        _materializer = Context.Materializer();
        _sseStarted = false;

        // Wire the response channel into the actor mailbox (safe — just plumbing, no auth needed)
        ChannelSource.FromReader(_client.Responses)
            .To(Sink.ActorRefWithAck<IHomeConnectMessage>(
                Self,
                StreamInit.Instance,
                StreamAck.Instance,
                StreamComplete.Instance,
                ex => new StreamFailed(ex)))
            .Run(_materializer);

        Log.Info("Home Connect client created (simulator={Simulator}), checking for existing tokens", useSimulator);

        // Check if we already have tokens — if so, start SSE immediately
        _ = TryStartWithExistingTokensAsync();
    }

    private async Task TryStartWithExistingTokensAsync()
    {
        try
        {
            await _oauthFlow!.GetAccessTokenAsync(CancellationToken.None);
            Self.Tell(BeginSseStream.Instance);
        }
        catch (InvalidOperationException)
        {
            Log.Info("No Home Connect tokens found — waiting for OAuth authorization via /api/oauth/homeconnect/authorize");
            PublishStatus("awaiting_auth", 0);
        }
    }

    private void StartSseStream()
    {
        if (_client is null || _sseStarted) return;

        _sseStarted = true;
        _client.TrySend(new SubscribeEventsCommand());
        Log.Info("Home Connect SSE stream started");
        PublishStatus("running", _haIdToDeviceId.Count);
        DiscoverAppliances();
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _oauthFlow?.Dispose();
        _oauthFlow = null;
        _sseStarted = false;
    }

    private void OnHomeConnectMessage(IHomeConnectMessage message)
    {
        switch (message)
        {
            case HomeConnectEventMessage evt:
                OnEvent(evt);
                break;
            case HomeConnectErrorMessage err:
                Log.Warning("Home Connect error: {Key} — {Description}", err.Key, err.Description);
                break;
            case SubscriptionDisconnectedMessage disc:
                Log.Warning(disc.Reason, "SSE subscription disconnected");
                break;
        }

        Sender.Tell(StreamAck.Instance);
    }

    private void OnEvent(HomeConnectEventMessage evt)
    {
        if (evt.HaId is null) return;

        switch (evt.Type)
        {
            case HomeConnectEventType.Notify:
            case HomeConnectEventType.Status:
            case HomeConnectEventType.Event:
                if (_haIdToDeviceId.TryGetValue(evt.HaId, out var deviceId) && evt.Items.Count > 0)
                {
                    var mappedItems = HomeConnectStateMapper.MapEventItems(evt.Items);
                    foreach (var (key, value) in mappedItems)
                        ShardProxy.Tell(new DeviceStateUpdate(deviceId, key, value));

                    if (evt.Type == HomeConnectEventType.Event)
                        ShardProxy.Tell(new DeviceStateUpdate(deviceId, "_eventType", "Event"));
                }
                break;

            case HomeConnectEventType.Connected:
                Log.Info("Appliance {HaId} came online", evt.HaId);
                break;

            case HomeConnectEventType.Disconnected:
                if (_haIdToDeviceId.TryGetValue(evt.HaId, out var offlineId))
                    ReportOffline(offlineId);
                break;

            case HomeConnectEventType.Paired:
            case HomeConnectEventType.Depaired:
                DiscoverAppliances();
                break;
        }
    }

    private void OnDeviceCommand(DeviceCommand cmd)
    {
        if (_client is null) return;
        if (!_deviceIdToHaId.TryGetValue(cmd.DeviceId, out var haId)) return;

        _ = DispatchCommandAsync(haId, cmd);
    }

    private async Task DispatchCommandAsync(string haId, DeviceCommand cmd)
    {
        try
        {
            if (cmd.Value is string action)
            {
                switch (action)
                {
                    case "stop":
                        await _client!.RequestAsync(new StopActiveProgramCommand(haId));
                        return;
                }
            }

            if (cmd.CapabilityKey == "program" && cmd.Value is IDictionary<string, object> extras)
            {
                if (extras.TryGetValue("programKey", out var keyObj) && keyObj is string programKey)
                {
                    await _client!.RequestAsync(new StartProgramCommand(haId, programKey));
                    return;
                }
            }

            Log.Warning("Unrecognized command for {HaId}: {CapabilityKey}={Value}", haId, cmd.CapabilityKey, cmd.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dispatch command to {HaId}", haId);
        }
    }

    private void OnOAuthCallback(OAuthCallbackReceived msg)
    {
        if (_oauthFlow is null)
        {
            Log.Warning("OAuth callback received but client is not initialized");
            return;
        }

        if (_sseStarted)
        {
            Log.Info("OAuth callback received but SSE is already running — ignoring");
            return;
        }

        Log.Info("OAuth callback received, exchanging code for tokens");
        _ = ExchangeCodeForTokensAsync(msg.Code, msg.RedirectUri);
    }

    private async Task ExchangeCodeForTokensAsync(string code, string redirectUri)
    {
        try
        {
            var settings = Settings;
            var useSimulator = settings.GetValueOrDefault("useSimulator", "false") == "true";
            var tokenEndpoint = useSimulator
                ? "https://simulator.home-connect.com/security/oauth/token"
                : settings.GetValueOrDefault("oauthTokenEndpoint",
                    "https://api.home-connect.com/security/oauth/token");

            var clientId = settings.GetValueOrDefault("clientId", "");
            var clientSecret = settings.GetValueOrDefault("clientSecret");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
            };
            if (!string.IsNullOrEmpty(clientSecret))
                form["client_secret"] = clientSecret;

            using var http = new HttpClient();
            var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form));
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Self.Tell(new TokenExchangeFailed($"Token endpoint returned {(int)response.StatusCode}: {body}"));
                return;
            }

            var json = JsonDocument.Parse(body).RootElement;
            var accessToken = json.GetProperty("access_token").GetString()!;
            var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var expiresIn = json.GetProperty("expires_in").GetInt32();

            var token = new PersistedToken(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));

            var tokenFilePath = settings.GetValueOrDefault("tokenFilePath", "/data/homeconnect-token.json");
            var store = new FileTokenStore(tokenFilePath);
            await store.SaveAsync(token, CancellationToken.None);

            Log.Info("OAuth tokens obtained and persisted, starting SSE stream");
            Self.Tell(BeginSseStream.Instance);
        }
        catch (Exception ex)
        {
            Self.Tell(new TokenExchangeFailed(ex.Message));
        }
    }

    private void OnWebhookReceived(WebhookReceived msg)
    {
        Log.Info("Webhook received on route {RouteKey}", msg.RouteKey);
    }

    private void DiscoverAppliances()
    {
        if (_client is null) return;
        _ = DiscoverAppliancesAsync();
    }

    private async Task DiscoverAppliancesAsync()
    {
        try
        {
            var response = await _client!.RequestAsync(new GetAppliancesCommand());
            foreach (var appliance in response.Appliances)
            {
                var discovered = HomeConnectApplianceMapper.Map(appliance);
                Mediator.Tell(new Publish("device-discovered", discovered));
                Log.Info("Discovered {Type} '{Name}' ({HaId})",
                    appliance.Type, appliance.Name, appliance.HaId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to discover Home Connect appliances");
        }
    }
}
