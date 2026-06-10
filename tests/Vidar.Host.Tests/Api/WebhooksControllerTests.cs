using System.Text;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Webhooks;
using Vidar.Host.Api;
using Vidar.Host.Persistence;
using Vidar.Host.Webhooks;

namespace Vidar.Host.Tests.Api;

public sealed class WebhooksControllerTests : TestKit
{
    private readonly IWebhookRouteCache _routes = Substitute.For<IWebhookRouteCache>();
    private readonly IWebhookPayloadRepository _payloads = Substitute.For<IWebhookPayloadRepository>();
    private readonly WebhooksController _sut;

    public WebhooksControllerTests()
    {
        var registry = Substitute.For<IRequiredActor<WebhookRegistry>>();
        registry.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TestActor));
        _sut = new WebhooksController(_routes, _payloads, registry)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void SetupRoute(string key, WebhookRouteInfo info) =>
        _routes.TryGetRoute(key, out Arg.Any<WebhookRouteInfo>()!)
            .Returns(x => { x[1] = info; return true; });

    private void SetBody(string content, string contentType = "application/json")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        _sut.Request.Body = new MemoryStream(bytes);
        _sut.Request.ContentLength = bytes.Length;
        _sut.Request.ContentType = contentType;
    }

    [Fact]
    public async Task Receive_UnknownRoute_Returns404_AndStoresNothing()
    {
        var result = await _sut.Receive("nope");

        Assert.IsType<NotFoundResult>(result);
        await _payloads.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Receive_NoAuth_StoresPayload_TellsRegistry_Returns200()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.None, null, null));
        SetBody("{\"alarm\":{}}");
        var payloadId = Guid.NewGuid();
        _payloads.StoreAsync("unifi-protect", "application/json", Arg.Any<Dictionary<string, string>>(), Arg.Any<Stream>())
            .Returns(payloadId);

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<OkResult>(result);
        ExpectMsg<WebhookReceived>(m =>
            m.RouteKey == "unifi-protect" &&
            m.PayloadId == payloadId &&
            m.ContentType == "application/json");
    }

    [Fact]
    public async Task Receive_UrlSecret_Wrong_Returns404()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.UrlSecret, "right", null));
        SetBody("{}");

        var result = await _sut.Receive("unifi-protect", "wrong");

        Assert.IsType<NotFoundResult>(result);
        await _payloads.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Receive_UrlSecret_Correct_Returns200()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.UrlSecret, "right", null));
        SetBody("{}");
        _payloads.StoreAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Guid.NewGuid());

        var result = await _sut.Receive("unifi-protect", "right");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Receive_HeaderToken_Correct_Returns200()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.HeaderToken, "tok", "X-Webhook-Token"));
        SetBody("{}");
        _sut.Request.Headers["X-Webhook-Token"] = "tok";
        _payloads.StoreAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Guid.NewGuid());

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Receive_HeaderToken_Missing_Returns404()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.HeaderToken, "tok", "X-Webhook-Token"));
        SetBody("{}");

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Receive_HeaderToken_Wrong_Returns404()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.HeaderToken, "tok", "X-Webhook-Token"));
        SetBody("{}");
        _sut.Request.Headers["X-Webhook-Token"] = "wrong";

        var result = await _sut.Receive("unifi-protect");

        Assert.IsType<NotFoundResult>(result);
        await _payloads.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Receive_BodyTooLarge_Returns413()
    {
        SetupRoute("unifi-protect", new WebhookRouteInfo(WebhookAuthMode.None, null, null));
        SetBody("{}");
        _sut.Request.ContentLength = 9L * 1024 * 1024; // over the 8 MB default

        var result = await _sut.Receive("unifi-protect");

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status.StatusCode);
    }

    [Fact]
    public async Task GetPayload_Missing_Returns404()
    {
        _payloads.OpenAsync(Arg.Any<Guid>()).Returns((WebhookPayload?)null);

        var result = await _sut.GetPayload(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPayload_Found_StreamsWithContentType()
    {
        var payloadId = Guid.NewGuid();
        var content = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));
        _payloads.OpenAsync(payloadId).Returns(new WebhookPayload(content, "application/json"));

        var result = await _sut.GetPayload(payloadId);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/json", file.ContentType);
    }
}
