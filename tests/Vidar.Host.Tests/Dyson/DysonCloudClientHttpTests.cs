using System.Net;
using System.Text;
using Vidar.Host.Dyson;
using Xunit;

namespace Vidar.Host.Tests.Dyson;

public class DysonCloudClientHttpTests
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

    private static string EncryptCreds() // reuse the encryptor from Task 1's test helper (copy inline)
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key; aes.IV = new byte[16];
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes("{\"apPasswordHash\":\"pw123\"}");
        return Convert.ToBase64String(enc.TransformFinalBlock(bytes, 0, bytes.Length));
    }

    [Fact]
    public async Task BeginLogin_PostsAuthAndReturnsChallengeId()
    {
        var handler = new StubHandler(_ => (HttpStatusCode.OK, "{\"challengeId\":\"abc-123\"}"));
        var client = new DysonCloudClient(new HttpClient(handler) { BaseAddress = new Uri("https://appapi.cp.dyson.com") });

        var challenge = await client.BeginLoginAsync("DE", "me@example.com", default);

        Assert.Equal("abc-123", challenge);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/v3/userregistration/email/auth");
    }

    [Fact]
    public async Task GetDevices_DecryptsLocalCredentials()
    {
        var creds = EncryptCreds();
        var manifest = $"[{{\"Serial\":\"X6p-EU-SKA0802A\",\"ProductType\":\"438\",\"Name\":\"Bedroom\",\"LocalCredentials\":\"{creds}\",\"variant\":null}}]";
        var handler = new StubHandler(_ => (HttpStatusCode.OK, manifest));
        var client = new DysonCloudClient(new HttpClient(handler) { BaseAddress = new Uri("https://appapi.cp.dyson.com") });

        var devices = await client.GetDevicesAsync("token", default);

        Assert.Single(devices);
        Assert.Equal("X6p-EU-SKA0802A", devices[0].Serial);
        Assert.Equal("438", devices[0].ProductType);
        Assert.Equal("pw123", devices[0].MqttPassword);
    }
}
