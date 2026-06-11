using Vidar.Core.Messages;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Webhooks;

public class WebhookRouteCacheTests
{
    private readonly WebhookRouteCache _sut = new();

    [Fact]
    public void TryGetRoute_UnknownKey_ReturnsFalse()
    {
        Assert.False(_sut.TryGetRoute("nope", out _));
    }

    [Fact]
    public void TryGetRoute_AfterUpdate_ReturnsRoute()
    {
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["unifi-protect"] = new(WebhookAuthMode.UrlSecret, "s3cret", null)
        });

        Assert.True(_sut.TryGetRoute("unifi-protect", out var route));
        Assert.Equal(WebhookAuthMode.UrlSecret, route.AuthMode);
        Assert.Equal("s3cret", route.Secret);
    }

    [Fact]
    public void UpdateRoutes_ReplacesEntireTable()
    {
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["a"] = new(WebhookAuthMode.None, null, null)
        });
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["b"] = new(WebhookAuthMode.None, null, null)
        });

        Assert.False(_sut.TryGetRoute("a", out _));
        Assert.True(_sut.TryGetRoute("b", out _));
    }

    [Fact]
    public void Snapshot_ReturnsCurrentRoutes()
    {
        _sut.UpdateRoutes(new Dictionary<string, WebhookRouteInfo>
        {
            ["unifi-protect"] = new(WebhookAuthMode.None, null, null, "unifi")
        });

        var snapshot = _sut.Snapshot();

        Assert.Single(snapshot);
        Assert.Equal("unifi", snapshot["unifi-protect"].IntegrationId);
    }
}
