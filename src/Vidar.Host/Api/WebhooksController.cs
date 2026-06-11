using System.Text.Json;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Core.Webhooks;
using Vidar.Host.Actors;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Api;

[ApiController]
public sealed class WebhooksController : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly long MaxBodyBytes =
        (long.TryParse(Environment.GetEnvironmentVariable("VIDAR_WEBHOOK_MAX_BODY_MB"), out var mb) ? mb : 8) * 1024 * 1024;

    private readonly IWebhookRouteCache _routes;
    private readonly IWebhookPayloadRepository _payloads;
    private readonly IWebhookEventRepository _events;
    private readonly IRequiredActor<WebhookRegistry> _registry;
    private readonly IRequiredActor<WebhookEventSseActor> _webhookSse;

    public WebhooksController(
        IWebhookRouteCache routes,
        IWebhookPayloadRepository payloads,
        IWebhookEventRepository events,
        IRequiredActor<WebhookRegistry> registry,
        IRequiredActor<WebhookEventSseActor> webhookSse)
    {
        _routes = routes;
        _payloads = payloads;
        _events = events;
        _registry = registry;
        _webhookSse = webhookSse;
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

        var integrationId = route.IntegrationId;
        var receivedAt = DateTimeOffset.UtcNow;
        await _events.InsertAsync(new WebhookEventDocument(
            payloadId, key, integrationId, contentType, Request.ContentLength ?? -1, receivedAt));

        var registry = await _registry.GetAsync();
        registry.Tell(new WebhookReceived(
            key, payloadId, headers, contentType, Request.ContentLength ?? -1, receivedAt));

        return Ok();
    }

    [HttpGet("/api/webhooks/payloads/{payloadId:guid}")]
    public async Task<IActionResult> GetPayload(Guid payloadId)
    {
        var payload = await _payloads.OpenAsync(payloadId);
        if (payload == null)
            return NotFound();
        // FileStreamResult takes ownership and disposes the GridFS stream after writing
        // the response — do not add code between OpenAsync and this return that could
        // throw or exit early without disposing the payload.
        return File(payload.Content, payload.ContentType);
    }

    [HttpGet("/api/webhooks/events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? routeKey = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(skip, 0);
        var page = await _events.ListAsync(routeKey, skip, take);
        return Ok(new WebhookEventPageResponse(
            page.Items.Select(e => new WebhookEventResponse(
                e.PayloadId, e.RouteKey, e.IntegrationId, e.ContentType, e.ContentLength, e.ReceivedAt,
                e.Status, e.HandledAt, e.Error)).ToList(),
            page.TotalCount));
    }

    [HttpGet("/api/webhooks/routes")]
    public IActionResult GetRoutes()
    {
        var routes = _routes.Snapshot()
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new WebhookRouteResponse(
                kv.Key,
                kv.Value.IntegrationId,
                kv.Value.AuthMode,
                kv.Value.AuthMode == WebhookAuthMode.UrlSecret && kv.Value.Secret != null
                    ? $"/webhooks/{kv.Key}/{kv.Value.Secret}"
                    : $"/webhooks/{kv.Key}",
                kv.Value.HeaderName))
            .ToList();
        return Ok(routes);
    }

    [HttpGet("/api/webhooks/events/stream")]
    public async Task StreamEvents([FromQuery] string? routeKey = null, CancellationToken cancellationToken = default)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var channel = Channel.CreateBounded<object>(100);
        var actor = _webhookSse.ActorRef;
        actor.Tell(new RegisterWebhookSseClient(channel));

        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
            {
                switch (msg)
                {
                    case WebhookReceivedNotification received:
                        if (routeKey != null && received.RouteKey != routeKey)
                            continue;
                        var receivedData = JsonSerializer.Serialize(new WebhookEventResponse(
                            received.PayloadId, received.RouteKey, received.IntegrationId,
                            received.ContentType, received.ContentLength, received.ReceivedAt,
                            "pending", null, null), SseJsonOptions);
                        await Response.WriteAsync($"data: {receivedData}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        break;

                    case WebhookHandledNotification handled:
                        var handledData = JsonSerializer.Serialize(new WebhookHandledResponse(
                            handled.PayloadId, handled.Status, handled.Error, handled.HandledAt), SseJsonOptions);
                        await Response.WriteAsync($"event: webhook-handled\ndata: {handledData}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                        break;
                }
            }
        }
        finally
        {
            actor.Tell(new UnregisterWebhookSseClient(channel));
        }
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
