using System.Net;
using System.Text;
using Vidar.Host.Dreo;
using Xunit;

namespace Vidar.Host.Tests.Dreo;

public class SidecarDreoAuthTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> r) => _responder = r;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (code, body) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task PasswordLoginAsync_MapsSidecarResponse_IncludingRegion()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK,
            "{\"userDataJson\":\"tok\",\"region\":\"eu\",\"devices\":[{\"serial\":\"S1\",\"model\":\"M1\",\"name\":\"Fan\"}]}"));
        var auth = new SidecarDreoAuth(new HttpClient(handler) { BaseAddress = new Uri("http://dreo2mqtt:8896") });

        var result = await auth.PasswordLoginAsync("me@example.com", "pw", default);

        Assert.Equal("tok", result.TokenJson);
        Assert.Equal("eu", result.Region);
        Assert.Single(result.Devices);
        Assert.Equal(new DreoManifestEntry("S1", "M1", "Fan"), result.Devices[0]);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/auth/login");
    }
}
