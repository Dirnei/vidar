using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vidar.Communication.Spotify.Tests;

// Minimal test seam over the protected SendAsync so stubs can capture requests.
public abstract class HttpRequestMessageHandlerBase : HttpMessageHandler
{
    protected abstract Task<HttpResponseMessage> HandleAsync(HttpRequestMessage req, CancellationToken ct);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        => HandleAsync(req, ct);
}
