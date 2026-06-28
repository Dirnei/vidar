using System.Net;
using System.Text;
using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonCloudIotTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> r) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null) await request.Content.LoadIntoBufferAsync();
            Requests.Add(request);
            var (code, body) = r(request);
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private const string Body = """
    {"Endpoint":"abc-ats.iot.eu-west-1.amazonaws.com",
     "IoTCredentials":{"ClientId":"cid-1","TokenValue":"tok-1","TokenKey":"token",
                       "TokenSignature":"sig-1","CustomAuthorizerName":"auth-1"}}
    """;

    [Fact]
    public async Task GetCredentials_PostsSerialWithBearer_ParsesResponse()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, Body));
        var client = new DysonCloudIot(new HttpClient(handler) { BaseAddress = new Uri("https://appapi.cp.dyson.com") });

        var creds = await client.GetCredentialsAsync("X6P-EU-SKA0802A", "acct-token", default);

        Assert.Equal("abc-ats.iot.eu-west-1.amazonaws.com", creds.Endpoint);
        Assert.Equal("cid-1", creds.ClientId);
        Assert.Equal("token", creds.TokenKey);
        Assert.Equal("tok-1", creds.TokenValue);
        Assert.Equal("sig-1", creds.TokenSignature);
        Assert.Equal("auth-1", creds.CustomAuthorizerName);

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/v2/authorize/iot-credentials", req.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("acct-token", req.Headers.Authorization.Parameter);
        var sentBody = await req.Content!.ReadAsStringAsync();
        // The Dyson API rejects camelCase "serial" with 400 — the body MUST be PascalCase "Serial".
        Assert.Contains("\"Serial\"", sentBody);
        Assert.Contains("X6P-EU-SKA0802A", sentBody);
    }

    [Fact]
    public async Task GetCredentials_On401_ThrowsAuthExpired()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.Unauthorized, "{}"));
        var client = new DysonCloudIot(new HttpClient(handler) { BaseAddress = new Uri("https://appapi.cp.dyson.com") });

        await Assert.ThrowsAsync<DysonAuthExpiredException>(
            () => client.GetCredentialsAsync("S1", "expired", default));
    }
}
