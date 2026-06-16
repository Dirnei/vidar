using System.Net.Http.Json;
using System.Text.Json;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHomeConnect;
using TurboHomeConnect.Abstractions;
using TurboHomeConnect.Commands;
using TurboHomeConnect.Model;
using TurboHomeConnect.OAuth;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;

namespace Vidar.Communication.HomeConnect;

public sealed class HomeConnectBridgeActor : ReceiveActor, IWithTimers
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _shardProxy;
    private readonly IActorRef _webhookRegistry;
    private readonly IActorRef _pluginRegistry;
    private readonly IActorRef _mediator;

    private readonly Dictionary<string, Guid> _haIdToDeviceId = new();
    private readonly Dictionary<Guid, string> _deviceIdToHaId = new();

    private IHomeConnectClient? _client;
    private HomeConnectAuthorizationCodeFlow? _oauthFlow;
    private IMaterializer? _materializer;
    private bool _sseStarted;

    private Dictionary<string, string> _settings = new();
    private bool _enabled;

    public ITimerScheduler Timers { get; set; } = null!;

    private sealed class StreamInit { public static readonly StreamInit Instance = new(); }
    private sealed class StreamAck { public static readonly StreamAck Instance = new(); }
    private sealed class StreamComplete { public static readonly StreamComplete Instance = new(); }
    private sealed record StreamFailed(Exception Cause);
    private sealed class BeginSseStream { public static readonly BeginSseStream Instance = new(); }
    private sealed record TokenExchangeFailed(string Error);

    public static Props Props(IActorRef shardProxy, IActorRef webhookRegistry, IActorRef pluginRegistry) =>
        Akka.Actor.Props.Create(() => new HomeConnectBridgeActor(shardProxy, webhookRegistry, pluginRegistry));

    public HomeConnectBridgeActor(IActorRef shardProxy, IActorRef webhookRegistry, IActorRef pluginRegistry)
    {
        _shardProxy = shardProxy;
        _webhookRegistry = webhookRegistry;
        _pluginRegistry = pluginRegistry;
        _mediator = DistributedPubSub.Get(Context.System).Mediator;

        Receive<IntegrationConfigChanged>(OnIntegrationConfigChanged);
        Receive<PluginRegistered>(msg =>
        {
            foreach (var device in msg.Registrations)
                RegisterDevice(device.DeviceId, device.NativeId);
            _log.Info("Plugin registered with {Count} devices, enabled={Enabled}",
                msg.Registrations.Count, msg.Enabled);

            _settings = msg.Settings;
            _enabled = msg.Enabled;

            if (_enabled)
                StartClient();
        });
        Receive<RegisterDeviceForPolling>(OnRegisterDevice);
        Receive<DeviceCommand>(OnDeviceCommand);
        Receive<WebhookReceived>(OnWebhookReceived);
        Receive<OAuthCallbackReceived>(OnOAuthCallback);
        Receive<BeginSseStream>(_ => StartSseStream());
        Receive<TokenExchangeFailed>(msg =>
        {
            _log.Error("OAuth token exchange failed: {Error}", msg.Error);
            PublishStatus("error", 0, $"OAuth token exchange failed: {msg.Error}");
        });

        // Akka.Streams flow messages
        Receive<StreamInit>(_ => Sender.Tell(StreamAck.Instance));
        Receive<IHomeConnectMessage>(OnHomeConnectMessage);
        Receive<StreamComplete>(_ => _log.Info("Home Connect event stream completed"));
        Receive<StreamFailed>(msg => _log.Error(msg.Cause, "Home Connect event stream failed"));
    }

    protected override void PreStart()
    {
        base.PreStart();
        _pluginRegistry.Tell(new RegisterPlugin("homeconnect", Self));

        _webhookRegistry.Tell(new RegisterWebhookListener(
            "oauth-homeconnect", Self, IntegrationId: "homeconnect"));
    }

    protected override void PostStop()
    {
        DisposeClient();
        base.PostStop();
    }

    private void OnIntegrationConfigChanged(IntegrationConfigChanged msg)
    {
        if (msg.IntegrationId != "homeconnect") return;

        _settings = msg.Settings;
        _enabled = msg.Enabled;

        if (_enabled)
            StartClient();
        else
            DisposeClient();
    }

    private void StartClient()
    {
        DisposeClient();

        var clientId = _settings.GetValueOrDefault("clientId", "");
        var clientSecret = _settings.GetValueOrDefault("clientSecret");
        var useSimulator = _settings.GetValueOrDefault("useSimulator", "false") == "true";
        var tokenFilePath = _settings.GetValueOrDefault("tokenFilePath", "/data/homeconnect-token.json");

        if (string.IsNullOrEmpty(clientId))
        {
            _log.Warning("Home Connect clientId not configured — staying idle");
            return;
        }

        var hostBaseUrl = _settings.GetValueOrDefault("hostBaseUrl", "http://vidar-host:8080");
        var redirectUri = new Uri($"{hostBaseUrl.TrimEnd('/')}/api/oauth/homeconnect/callback");

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

        _log.Info("Home Connect client created (simulator={Simulator}), checking for existing tokens", useSimulator);

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
            _log.Info("No Home Connect tokens found — waiting for OAuth authorization via /api/oauth/homeconnect/authorize");
            PublishStatus("awaiting_auth", 0);
        }
    }

    private void StartSseStream()
    {
        if (_client is null || _sseStarted) return;

        _sseStarted = true;
        _client.TrySend(new SubscribeEventsCommand());
        _log.Info("Home Connect SSE stream started");
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

    private void PublishStatus(string status, int deviceCount, string? error = null)
    {
        _mediator.Tell(new Publish("application-status",
            new ApplicationStatusUpdate("homeconnect", status, deviceCount, error)));
    }

    private void OnRegisterDevice(RegisterDeviceForPolling msg)
    {
        RegisterDevice(msg.DeviceId, msg.NativeId);
    }

    private void RegisterDevice(Guid deviceId, string haId)
    {
        _haIdToDeviceId[haId] = deviceId;
        _deviceIdToHaId[deviceId] = haId;
        _log.Info("Registered device {DeviceId} → {HaId}", deviceId, haId);
    }

    private void OnHomeConnectMessage(IHomeConnectMessage message)
    {
        switch (message)
        {
            case HomeConnectEventMessage evt:
                OnEvent(evt);
                break;
            case HomeConnectErrorMessage err:
                _log.Warning("Home Connect error: {Key} — {Description}", err.Key, err.Description);
                break;
            case SubscriptionDisconnectedMessage disc:
                _log.Warning(disc.Reason, "SSE subscription disconnected");
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
                    var extras = HomeConnectStateMapper.MapEventItems(evt.Items);
                    if (evt.Type == HomeConnectEventType.Event)
                        extras["_eventType"] = "Event";
                    _shardProxy.Tell(new DeviceStateUpdate(deviceId, CapabilityType.Extras, extras));
                }
                break;

            case HomeConnectEventType.Connected:
                _log.Info("Appliance {HaId} came online", evt.HaId);
                break;

            case HomeConnectEventType.Disconnected:
                if (_haIdToDeviceId.TryGetValue(evt.HaId, out var offlineId))
                    _shardProxy.Tell(new DeviceOffline(offlineId));
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

            if (cmd.Capability == CapabilityType.Extras && cmd.Value is IDictionary<string, object> extras)
            {
                if (extras.TryGetValue("programKey", out var keyObj) && keyObj is string programKey)
                {
                    await _client!.RequestAsync(new StartProgramCommand(haId, programKey));
                    return;
                }
            }

            _log.Warning("Unrecognized command for {HaId}: {Capability}={Value}", haId, cmd.Capability, cmd.Value);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to dispatch command to {HaId}", haId);
        }
    }

    private void OnOAuthCallback(OAuthCallbackReceived msg)
    {
        if (_oauthFlow is null)
        {
            _log.Warning("OAuth callback received but client is not initialized");
            return;
        }

        if (_sseStarted)
        {
            _log.Info("OAuth callback received but SSE is already running — ignoring");
            return;
        }

        _log.Info("OAuth callback received, exchanging code for tokens");
        _ = ExchangeCodeForTokensAsync(msg.Code);
    }

    private async Task ExchangeCodeForTokensAsync(string code)
    {
        try
        {
            var useSimulator = _settings.GetValueOrDefault("useSimulator", "false") == "true";
            var tokenEndpoint = useSimulator
                ? "https://simulator.home-connect.com/security/oauth/token"
                : _settings.GetValueOrDefault("oauthTokenEndpoint",
                    "https://api.home-connect.com/security/oauth/token");

            var clientId = _settings.GetValueOrDefault("clientId", "");
            var clientSecret = _settings.GetValueOrDefault("clientSecret");
            var hostBaseUrl = _settings.GetValueOrDefault("hostBaseUrl", "http://vidar-host:8080");
            var redirectUri = $"{hostBaseUrl.TrimEnd('/')}/api/oauth/homeconnect/callback";

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

            var tokenFilePath = _settings.GetValueOrDefault("tokenFilePath", "/data/homeconnect-token.json");
            var store = new FileTokenStore(tokenFilePath);
            await store.SaveAsync(token, CancellationToken.None);

            _log.Info("OAuth tokens obtained and persisted, starting SSE stream");
            Self.Tell(BeginSseStream.Instance);
        }
        catch (Exception ex)
        {
            Self.Tell(new TokenExchangeFailed(ex.Message));
        }
    }

    private void OnWebhookReceived(WebhookReceived msg)
    {
        _log.Info("Webhook received on route {RouteKey}", msg.RouteKey);
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
                _mediator.Tell(new Publish("device-discovered", discovered));
                _log.Info("Discovered {Type} '{Name}' ({HaId})",
                    appliance.Type, appliance.Name, appliance.HaId);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to discover Home Connect appliances");
        }
    }
}
