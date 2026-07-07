using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vidar.Communication.Spotify;
using Xunit;

namespace Vidar.Communication.Spotify.Tests;

public class SpotifyOAuthTests
{
    private sealed class StubHandler : HttpRequestMessageHandlerBase
    {
        public string Response = """{"access_token":"AT","refresh_token":"RT","expires_in":3600}""";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public HttpRequestMessage? Last;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Last = req;
            LastBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }

    private static (SpotifyOAuth, SpotifyTokenStore, StubHandler, string) Make()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tok-{Guid.NewGuid():N}.json");
        var stub = new StubHandler();
        var http = new HttpClient(stub);
        var store = new SpotifyTokenStore(path);
        var oauth = new SpotifyOAuth(http, store, "cid", "secret", "http://cb", "https://token");
        return (oauth, store, stub, path);
    }

    [Fact]
    public async Task ExchangeCode_PersistsToken_WithBasicAuth()
    {
        var (oauth, store, stub, path) = Make();
        try
        {
            await oauth.ExchangeCodeAsync("thecode", CancellationToken.None);
            var tok = await store.LoadAsync();
            Assert.Equal("AT", tok!.AccessToken);
            Assert.Equal("RT", tok.RefreshToken);
            Assert.Equal("Basic", stub.Last!.Headers.Authorization!.Scheme);
            Assert.Contains("grant_type=authorization_code", stub.LastBody);
            Assert.Contains("code=thecode", stub.LastBody);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task GetAccessToken_ValidToken_ReturnsWithoutRefresh()
    {
        var (oauth, store, stub, path) = Make();
        try
        {
            await store.SaveAsync(new SpotifyToken("STILLGOOD", "RT", DateTimeOffset.UtcNow.AddMinutes(30)));
            stub.Last = null;
            var t = await oauth.GetAccessTokenAsync(CancellationToken.None);
            Assert.Equal("STILLGOOD", t);
            Assert.Null(stub.Last); // no HTTP call
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task GetAccessToken_Expired_Refreshes()
    {
        var (oauth, store, stub, path) = Make();
        try
        {
            await store.SaveAsync(new SpotifyToken("OLD", "RT", DateTimeOffset.UtcNow.AddSeconds(10)));
            stub.Response = """{"access_token":"NEW","expires_in":3600}""";
            var t = await oauth.GetAccessTokenAsync(CancellationToken.None);
            Assert.Equal("NEW", t);
            Assert.Contains("grant_type=refresh_token", stub.LastBody);
            var persisted = await store.LoadAsync();
            Assert.Equal("NEW", persisted!.AccessToken);
            Assert.Equal("RT", persisted.RefreshToken); // preserved when refresh omits it
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task GetAccessToken_NoToken_ReturnsNull()
    {
        var (oauth, _, _, path) = Make();
        try { Assert.Null(await oauth.GetAccessTokenAsync(CancellationToken.None)); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
