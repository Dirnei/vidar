using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Webhooks;
using Vidar.Host.Api;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Api;

public sealed class OAuthControllerTests : TestKit
{
    private readonly IApplicationConfigRepository _repo = Substitute.For<IApplicationConfigRepository>();
    private readonly OAuthController _sut;

    public OAuthControllerTests()
    {
        var registry = Substitute.For<IRequiredActor<WebhookRegistry>>();
        registry.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TestActor));

        _sut = new OAuthController(_repo, registry)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        _sut.Request.Scheme = "https";
        _sut.Request.Host = new HostString("vidar.local", 443);

        // Clear any state left from previous tests.
        OAuthController.PendingStates.Clear();
    }

    private ApplicationConfig MakeConfig(Dictionary<string, string>? settings = null) => new()
    {
        Id = "homeconnect",
        Name = "Home Connect",
        ApplicationType = ApplicationType.Provider,
        Enabled = true,
        Settings = settings ?? new Dictionary<string, string>
        {
            ["clientId"] = "my-client-id",
            ["oauthAuthorizeEndpoint"] = "https://api.home-connect.com/security/oauth/authorize",
            ["oauthScopes"] = "IdentifyAppliance Monitor",
        }
    };

    // ── Authorize endpoint ──────────────────────────────────────────────

    [Fact]
    public async Task Authorize_UnknownIntegration_Returns404()
    {
        var result = await _sut.Authorize("unknown");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Authorize_NoConfig_Returns400()
    {
        _repo.GetByIdAsync("homeconnect").Returns((ApplicationConfig?)null);

        var result = await _sut.Authorize("homeconnect");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Authorize_MissingClientId_Returns400()
    {
        var settings = new Dictionary<string, string>
        {
            ["oauthAuthorizeEndpoint"] = "https://example.com/authorize",
            ["oauthScopes"] = "read",
        };
        _repo.GetByIdAsync("homeconnect").Returns(MakeConfig(settings));

        var result = await _sut.Authorize("homeconnect");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Authorize_MissingAuthorizeEndpoint_Returns400()
    {
        var settings = new Dictionary<string, string>
        {
            ["clientId"] = "my-client",
            ["oauthScopes"] = "read",
        };
        _repo.GetByIdAsync("homeconnect").Returns(MakeConfig(settings));

        var result = await _sut.Authorize("homeconnect");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Authorize_MissingScopes_Returns400()
    {
        var settings = new Dictionary<string, string>
        {
            ["clientId"] = "my-client",
            ["oauthAuthorizeEndpoint"] = "https://example.com/authorize",
        };
        _repo.GetByIdAsync("homeconnect").Returns(MakeConfig(settings));

        var result = await _sut.Authorize("homeconnect");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Authorize_Spotify_NoAuthorizeEndpoint_FallsBackToDefault()
    {
        // Spotify's authorize endpoint is a fixed constant, so it is not a config field; the
        // controller supplies the default. Only clientId + scopes are persisted.
        var config = new ApplicationConfig
        {
            Id = "spotify",
            Name = "Spotify",
            ApplicationType = ApplicationType.Provider,
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["clientId"] = "spotify-client",
                ["oauthScopes"] = "user-read-playback-state",
            },
        };
        _repo.GetByIdAsync("spotify").Returns(config);

        var result = await _sut.Authorize("spotify");

        var ok = Assert.IsType<OkObjectResult>(result);
        var url = ok.Value!.GetType().GetProperty("authorizeUrl")!.GetValue(ok.Value) as string;
        Assert.NotNull(url);
        Assert.StartsWith("https://accounts.spotify.com/authorize?", url);
        Assert.Contains("client_id=spotify-client", url);
        Assert.Contains("api%2Foauth%2Fspotify%2Fcallback", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorize_ValidConfig_ReturnsAuthorizeUrl()
    {
        _repo.GetByIdAsync("homeconnect").Returns(MakeConfig());

        var result = await _sut.Authorize("homeconnect");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        // Extract the authorizeUrl from the anonymous object.
        var urlProp = ok.Value.GetType().GetProperty("authorizeUrl");
        Assert.NotNull(urlProp);
        var url = urlProp.GetValue(ok.Value) as string;
        Assert.NotNull(url);

        Assert.StartsWith("https://api.home-connect.com/security/oauth/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=my-client-id", url);
        Assert.Contains("redirect_uri=", url);
        Assert.Contains("scope=IdentifyAppliance%20Monitor", url);
        Assert.Contains("state=", url);

        // Redirect URI should point at our callback.
        Assert.Contains("api%2Foauth%2Fhomeconnect%2Fcallback", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorize_StoresStateForIntegration()
    {
        _repo.GetByIdAsync("homeconnect").Returns(MakeConfig());

        await _sut.Authorize("homeconnect");

        Assert.True(OAuthController.PendingStates.ContainsKey("homeconnect"));
        Assert.False(string.IsNullOrEmpty(OAuthController.PendingStates["homeconnect"]));
    }

    // ── Callback endpoint ───────────────────────────────────────────────

    [Fact]
    public async Task Callback_UnknownIntegration_Returns404()
    {
        var result = await _sut.Callback("unknown", "code", "state");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Callback_MissingCode_Returns400()
    {
        var result = await _sut.Callback("homeconnect", null, "state");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_EmptyCode_Returns400()
    {
        var result = await _sut.Callback("homeconnect", "", "state");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_NoPendingState_Returns400()
    {
        // No state was stored for this integration.
        var result = await _sut.Callback("homeconnect", "auth-code", "some-state");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_InvalidState_Returns400()
    {
        OAuthController.PendingStates["homeconnect"] = "correct-state";

        var result = await _sut.Callback("homeconnect", "auth-code", "wrong-state");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_InvalidState_ClearsStoredState()
    {
        OAuthController.PendingStates["homeconnect"] = "correct-state";

        await _sut.Callback("homeconnect", "auth-code", "wrong-state");

        // State is removed by TryRemove even on mismatch.
        Assert.False(OAuthController.PendingStates.ContainsKey("homeconnect"));
    }

    [Fact]
    public async Task Callback_ValidCodeAndState_SendsMessageToRegistry()
    {
        OAuthController.PendingStates["homeconnect"] = "the-state";

        var result = await _sut.Callback("homeconnect", "auth-code", "the-state");

        var msg = ExpectMsg<OAuthCallbackReceived>();
        Assert.Equal("homeconnect", msg.IntegrationId);
        Assert.Equal("auth-code", msg.Code);
        Assert.Equal("the-state", msg.State);
    }

    [Fact]
    public async Task Callback_ValidCodeAndState_ReturnsHtml()
    {
        OAuthController.PendingStates["homeconnect"] = "the-state";

        var result = await _sut.Callback("homeconnect", "auth-code", "the-state");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/html", content.ContentType);
        Assert.Contains("Authorization complete", content.Content);
    }

    [Fact]
    public async Task Callback_ValidCodeAndState_ClearsStoredState()
    {
        OAuthController.PendingStates["homeconnect"] = "the-state";

        await _sut.Callback("homeconnect", "auth-code", "the-state");

        Assert.False(OAuthController.PendingStates.ContainsKey("homeconnect"));
    }
}
