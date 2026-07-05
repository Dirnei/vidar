using System.Net;
using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Dreo;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Dreo;

public class DreoControllerTests
{
    private sealed class FakeRepo : IApplicationConfigRepository
    {
        public ApplicationConfig? Saved;
        public Task<ApplicationConfig?> GetByIdAsync(string id) => Task.FromResult<ApplicationConfig?>(Saved);
        public Task<List<ApplicationConfig>> GetAllAsync() => Task.FromResult(new List<ApplicationConfig>());
        public Task UpsertAsync(ApplicationConfig c) { Saved = c; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private static (DreoController controller, FakeRepo repo, IDreoAuth auth, IActorRef pluginRegistry)
        CreateController(ApplicationConfig? preloaded = null)
    {
        var repo = new FakeRepo { Saved = preloaded };
        var auth = Substitute.For<IDreoAuth>();
        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));
        return (new DreoController(auth, repo, pluginRegistryProvider), repo, auth, actorRef);
    }

    [Fact]
    public async Task PasswordLogin_PersistsTokenRegionManifestAndEnables()
    {
        var (controller, repo, auth, pluginRegistry) = CreateController();
        var authResult = new DreoAuthResult(
            "the-token-json",
            "eu",
            new[] { new DreoManifestEntry("S1", "M1", "Fan") });
        auth.PasswordLoginAsync("user@test.com", "pass123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(authResult));

        var result = await controller.Login(new DreoLoginRequest("user@test.com", "pass123"), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.Enabled);
        Assert.Equal("the-token-json", repo.Saved.Settings["account.token"]);
        Assert.Equal("eu", repo.Saved.Settings["account.region"]);
        var manifest = JsonDocument.Parse(repo.Saved.Settings["account.manifest"]).RootElement;
        Assert.Equal("S1", manifest[0].GetProperty("serial").GetString());
        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "dreo" && r.Message is IntegrationConfigChanged),
            Arg.Any<IActorRef>());
    }

    [Fact]
    public async Task Account_ReturnsNotConnected_WhenNoConfig()
    {
        var (controller, _, _, _) = CreateController();

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Account_ReturnsConnected_WhenConfigured()
    {
        var manifest = JsonSerializer.Serialize(new[] { new { serial = "S1" } });
        var preloaded = new ApplicationConfig
        {
            Id = "dreo", Name = "Dreo", Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["account.email"] = "me@example.com",
                ["account.manifest"] = manifest,
            },
        };
        var (controller, _, _, _) = CreateController(preloaded);

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal("me@example.com", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("deviceCount").GetInt32());
    }

    [Fact]
    public async Task Account_ReturnsNotConnected_WhenConfigDisabled()
    {
        var preloaded = new ApplicationConfig
        {
            Id = "dreo", Name = "Dreo", Enabled = false,
            Settings = new Dictionary<string, string> { ["account.email"] = "me@example.com" },
        };
        var (controller, _, _, _) = CreateController(preloaded);

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Login_Returns401_WhenAuthRejects()
    {
        var (controller, _, auth, _) = CreateController();
        auth.PasswordLoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<DreoAuthResult>(_ => throw new HttpRequestException("nope", null, HttpStatusCode.Unauthorized));

        var result = await controller.Login(new DreoLoginRequest("user@test.com", "wrong"), default);

        Assert.Equal(401, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Login_Returns429_WhenAuthRateLimited()
    {
        var (controller, _, auth, _) = CreateController();
        auth.PasswordLoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<DreoAuthResult>(_ => throw new HttpRequestException("slow down", null, HttpStatusCode.TooManyRequests));

        var result = await controller.Login(new DreoLoginRequest("user@test.com", "pass123"), default);

        Assert.Equal(429, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
