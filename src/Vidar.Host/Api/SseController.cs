using System.Text.Json;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Vidar.Core.Messages;
using Vidar.Host.Actors;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/sse")]
public sealed class SseController : ControllerBase
{
    private readonly IRequiredActor<SseManagerActor> _sseManager;

    public SseController(IRequiredActor<SseManagerActor> sseManager)
    {
        _sseManager = sseManager;
    }

    [HttpGet("state")]
    public async Task StreamState(CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var channel = Channel.CreateBounded<DeviceStateChanged>(100);
        var manager = _sseManager.ActorRef;
        manager.Tell(new RegisterSseClient(channel));

        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var data = JsonSerializer.Serialize(new
                {
                    deviceId = msg.DeviceId,
                    capability = msg.CapabilityKey,
                    value = msg.Value,
                    timestamp = msg.Timestamp
                });
                await Response.WriteAsync($"event: deviceStateChanged\ndata: {data}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            manager.Tell(new UnregisterSseClient(channel));
        }
    }
}
