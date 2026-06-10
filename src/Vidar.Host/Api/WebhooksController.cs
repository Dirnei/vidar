using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Webhooks;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Api;

[ApiController]
public sealed class WebhooksController : ControllerBase
{
    private static readonly long MaxBodyBytes =
        long.Parse(Environment.GetEnvironmentVariable("VIDAR_WEBHOOK_MAX_BODY_MB") ?? "8") * 1024 * 1024;

    private readonly IWebhookRouteCache _routes;
    private readonly IWebhookPayloadRepository _payloads;
    private readonly IRequiredActor<WebhookRegistry> _registry;

    public WebhooksController(
        IWebhookRouteCache routes,
        IWebhookPayloadRepository payloads,
        IRequiredActor<WebhookRegistry> registry)
    {
        _routes = routes;
        _payloads = payloads;
        _registry = registry;
    }

    [HttpPost("/webhooks/{key}")]
    [HttpPost("/webhooks/{key}/{secret}")]
    public async Task<IActionResult> Receive(string key, string? secret = null)
    {
        // Unknown route and failed auth both answer 404 — don't leak which routes exist.
        if (!_routes.TryGetRoute(key, out var route))
            return NotFound();
        if (!IsAuthorized(route, secret))
            return NotFound();
        if (Request.ContentLength > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Enforce the cap for chunked requests without a Content-Length, too.
        var sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
            sizeFeature.MaxRequestBodySize = MaxBodyBytes;

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        var contentType = Request.ContentType ?? "application/octet-stream";

        Guid payloadId;
        try
        {
            payloadId = await _payloads.StoreAsync(key, contentType, headers, Request.Body);
        }
        catch (BadHttpRequestException)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var registry = await _registry.GetAsync();
        registry.Tell(new WebhookReceived(
            key, payloadId, headers, contentType, Request.ContentLength ?? -1, DateTimeOffset.UtcNow));

        return Ok();
    }

    [HttpGet("/api/webhooks/payloads/{payloadId:guid}")]
    public async Task<IActionResult> GetPayload(Guid payloadId)
    {
        var payload = await _payloads.OpenAsync(payloadId);
        if (payload == null)
            return NotFound();
        return File(payload.Content, payload.ContentType);
    }

    private bool IsAuthorized(WebhookRouteInfo route, string? urlSecret) => route.AuthMode switch
    {
        WebhookAuthMode.None => true,
        WebhookAuthMode.UrlSecret => urlSecret != null && urlSecret == route.Secret,
        WebhookAuthMode.HeaderToken => route.HeaderName != null &&
            Request.Headers.TryGetValue(route.HeaderName, out var value) &&
            value.ToString() == route.Secret,
        _ => false
    };
}
