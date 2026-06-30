using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Persistence;
using Vidar.Host.Roborock;

namespace Vidar.Host.Tests.Roborock;

public class RoborockControllerTests
{
    private sealed class FakeRepo : IApplicationConfigRepository
    {
        public ApplicationConfig? Saved;
        public Task<ApplicationConfig?> GetByIdAsync(string id) => Task.FromResult<ApplicationConfig?>(Saved);
        public Task<List<ApplicationConfig>> GetAllAsync() => Task.FromResult(new List<ApplicationConfig>());
        public Task UpsertAsync(ApplicationConfig c) { Saved = c; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private static (RoborockController controller, FakeRepo repo, IRoborockAuth auth, IActorRef pluginRegistry)
        CreateController(ApplicationConfig? preloaded = null)
    {
        var repo = new FakeRepo { Saved = preloaded };
        var auth = Substitute.For<IRoborockAuth>();
        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));
        return (new RoborockController(auth, repo, pluginRegistryProvider), repo, auth, actorRef);
    }

    [Fact]
    public async Task PasswordLogin_PersistsUserDataManifestAndEnables()
    {
        var (controller, repo, auth, pluginRegistry) = CreateController();
        var authResult = new RoborockAuthResult(
            "the-user-data-json",
            new[] { new RoborockManifestEntry("duid-1", "Robot 1", "S7", "localkey123", "192.168.1.100") });
        auth.PasswordLoginAsync("user@test.com", "pass123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(authResult));

        var result = await controller.Login(new RoborockLoginRequest("user@test.com", "pass123"), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.Enabled);
        Assert.Equal("the-user-data-json", repo.Saved.Settings["account.userData"]);
        var manifest = JsonDocument.Parse(repo.Saved.Settings["account.manifest"]).RootElement;
        Assert.Equal("duid-1", manifest[0].GetProperty("duid").GetString());
        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "roborock" && r.Message is IntegrationConfigChanged),
            Arg.Any<IActorRef>());
    }

    [Fact]
    public async Task RequestCode_DelegatesToAuth()
    {
        var (controller, _, auth, _) = CreateController();
        auth.RequestCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await controller.RequestCode(new RoborockEmailRequest("a@b.c"), default);

        await auth.Received(1).RequestCodeAsync("a@b.c", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodeLogin_PersistsManifest()
    {
        var (controller, repo, auth, pluginRegistry) = CreateController();
        var authResult = new RoborockAuthResult(
            "code-user-data",
            new[] { new RoborockManifestEntry("duid-2", "Robot 2", "Q5", "key456", "192.168.1.101") });
        auth.CodeLoginAsync("user@test.com", "123456", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(authResult));

        var result = await controller.CodeLogin(new RoborockCodeRequest("user@test.com", "123456"), default);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(repo.Saved);
        var manifest = JsonDocument.Parse(repo.Saved!.Settings["account.manifest"]).RootElement;
        Assert.Equal("duid-2", manifest[0].GetProperty("duid").GetString());
        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "roborock" && r.Message is IntegrationConfigChanged),
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
        var manifest = JsonSerializer.Serialize(new[] { new { duid = "duid-1" } });
        var preloaded = new ApplicationConfig
        {
            Id = "roborock", Name = "Roborock", Enabled = true,
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
}
