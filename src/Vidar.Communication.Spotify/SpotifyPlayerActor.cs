using System.Net;
using System.Net.Http;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Vidar.Core.Messages;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Spotify;

// One actor for the single Spotify Player device. Owns the HttpClient + token access and polls
// the Web API (Spotify has no push). Maps player + device-list responses to DeviceStateUpdates
// and dispatches transport/volume/zone commands.
public sealed class SpotifyPlayerActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private const string ApiBase = "https://api.spotify.com/v1";
    private static readonly TimeSpan PlayerInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DevicesInterval = TimeSpan.FromSeconds(20);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _shardProxy;
    private readonly Guid _deviceId;
    private readonly SpotifyOAuth _oauth;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private sealed class PollPlayer { public static readonly PollPlayer Instance = new(); }
    private sealed class PollDevices { public static readonly PollDevices Instance = new(); }
    private sealed record StateTuples(IReadOnlyList<(string Key, object Value)> Updates);

    public static Props Props(Guid deviceId, SpotifyOAuth oauth) =>
        Akka.Actor.Props.Create(() => new SpotifyPlayerActor(deviceId, oauth));

    public SpotifyPlayerActor(Guid deviceId, SpotifyOAuth oauth)
    {
        _deviceId = deviceId;
        _oauth = oauth;
        _shardProxy = ActorRegistry.For(Context.System).Get<DeviceTwinRegion>();

        ReceiveAsync<PollPlayer>(async _ =>
        {
            var body = await GetAsync("/me/player");
            if (body is not null) Self.Tell(new StateTuples(SpotifyStateMapper.MapPlayer(body)));
        });

        ReceiveAsync<PollDevices>(async _ =>
        {
            var body = await GetAsync("/me/player/devices");
            if (body is not null) Self.Tell(new StateTuples(SpotifyDeviceListMapper.MapDevices(body)));
        });

        Receive<StateTuples>(m =>
        {
            foreach (var (key, value) in m.Updates)
                _shardProxy.Tell(new DeviceStateUpdate(_deviceId, key, value));
        });

        ReceiveAsync<DeviceCommand>(OnCommandAsync);
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer("poll-player", PollPlayer.Instance, TimeSpan.FromSeconds(1), PlayerInterval);
        Timers.StartPeriodicTimer("poll-devices", PollDevices.Instance, TimeSpan.FromSeconds(2), DevicesInterval);
    }

    protected override void PostStop() => _http.Dispose();

    // Returns the response body ("" for 204), or null when the call could not be made/authorized.
    // The whole body — including the token fetch — is wrapped in try/catch so a token-store
    // failure (e.g. a transient file read error) can never fault this actor.
    private async Task<string?> GetAsync(string path)
    {
        try
        {
            var token = await _oauth.GetAccessTokenAsync(CancellationToken.None);
            if (token is null) return null;

            using var req = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, CancellationToken.None);
            if (resp.StatusCode == HttpStatusCode.NoContent) return "";
            if (resp.StatusCode == (HttpStatusCode)429) { _log.Debug("Spotify rate-limited on {Path}", path); return null; }
            if (!resp.IsSuccessStatusCode) { _log.Warning("Spotify GET {Path} → {Code}", path, (int)resp.StatusCode); return null; }
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { _log.Warning(ex, "Spotify GET {Path} failed", path); return null; }
    }

    private async Task OnCommandAsync(DeviceCommand cmd)
    {
        var spec = SpotifyCommandBuilder.Build(cmd.CapabilityKey, cmd.Value);
        if (spec is null) { _log.Warning("Unrecognized Spotify command {Key}={Value}", cmd.CapabilityKey, cmd.Value); return; }

        var token = await _oauth.GetAccessTokenAsync(CancellationToken.None);
        if (token is null) { _log.Warning("Spotify command dropped — no access token"); return; }

        var url = ApiBase + spec.Path;
        var query = string.Join("&", spec.Query.Where(kv => kv.Value is not null).Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        if (query.Length > 0) url += "?" + query;

        using var req = new HttpRequestMessage(spec.Method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (spec.JsonBody is not null)
            req.Content = new StringContent(spec.JsonBody, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.SendAsync(req, CancellationToken.None);
            if (resp.StatusCode == HttpStatusCode.Forbidden)
                _log.Warning("Spotify command {Key} forbidden (403) — Spotify Premium is required", cmd.CapabilityKey);
            else if (!resp.IsSuccessStatusCode)
                _log.Warning("Spotify command {Key} → {Code}", cmd.CapabilityKey, (int)resp.StatusCode);
            else
                Self.Tell(PollPlayer.Instance); // reflect the change quickly
        }
        catch (Exception ex) { _log.Warning(ex, "Spotify command {Key} failed", cmd.CapabilityKey); }
    }
}
