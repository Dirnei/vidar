using System.Collections.Concurrent;
using System.Security.Cryptography;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Vidar.Core.Messages;
using Vidar.Core.Webhooks;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/oauth")]
public sealed class OAuthController : ControllerBase
{
    private readonly IApplicationConfigRepository _repo;
    private readonly IRequiredActor<WebhookRegistry> _registryProvider;

    private static readonly HashSet<string> KnownIntegrations = ["homeconnect"];

    // Stores CSRF state per integration so the callback can verify it.
    internal static readonly ConcurrentDictionary<string, string> PendingStates = new();

    public OAuthController(
        IApplicationConfigRepository repo,
        IRequiredActor<WebhookRegistry> registryProvider)
    {
        _repo = repo;
        _registryProvider = registryProvider;
    }

    [HttpGet("{integrationId}/authorize")]
    public async Task<IActionResult> Authorize(string integrationId)
    {
        if (!KnownIntegrations.Contains(integrationId))
            return NotFound();

        var config = await _repo.GetByIdAsync(integrationId);
        if (config is null)
            return BadRequest(new { error = "No configuration found for this integration." });

        if (!config.Settings.TryGetValue("clientId", out var clientId) || string.IsNullOrEmpty(clientId))
            return BadRequest(new { error = "Missing 'clientId' in integration settings." });

        if (!config.Settings.TryGetValue("oauthAuthorizeEndpoint", out var authorizeEndpoint) || string.IsNullOrEmpty(authorizeEndpoint))
            return BadRequest(new { error = "Missing 'oauthAuthorizeEndpoint' in integration settings." });

        if (!config.Settings.TryGetValue("oauthScopes", out var scopes) || string.IsNullOrEmpty(scopes))
            return BadRequest(new { error = "Missing 'oauthScopes' in integration settings." });

        var state = GenerateState();
        PendingStates[integrationId] = state;

        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/oauth/{integrationId}/callback";

        var authorizeUrl = QueryHelpers.AddQueryString(authorizeEndpoint, new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scopes,
            ["state"] = state,
        });

        return Ok(new { authorizeUrl });
    }

    [HttpGet("{integrationId}/callback")]
    public async Task<IActionResult> Callback(
        string integrationId,
        [FromQuery] string? code,
        [FromQuery] string? state)
    {
        if (!KnownIntegrations.Contains(integrationId))
            return NotFound();

        if (string.IsNullOrEmpty(code))
            return BadRequest("Missing authorization code.");

        if (!PendingStates.TryRemove(integrationId, out var expectedState))
            return BadRequest("No pending OAuth state for this integration.");

        if (state != expectedState)
            return BadRequest("Invalid OAuth state (CSRF mismatch).");

        var registry = await _registryProvider.GetAsync();
        registry.Tell(new OAuthCallbackReceived(integrationId, code, state, DateTimeOffset.UtcNow));

        return new ContentResult
        {
            Content = """
                <!DOCTYPE html>
                <html><head><title>Vidar</title></head>
                <body style="font-family:sans-serif;text-align:center;padding-top:80px">
                <h2>Authorization complete</h2>
                <p>You can close this window.</p>
                </body></html>
                """,
            ContentType = "text/html",
            StatusCode = 200,
        };
    }

    private static string GenerateState()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
