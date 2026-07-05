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
using Xunit;

namespace Vidar.Host.Tests;

public class BambuControllerTests
{
    [Fact]
    public void UpsertManifest_AddsPrinter_PreservingExisting()
    {
        var existing = """[{"host":"1.1.1.1","serial":"A","accessCode":"x","model":"BL-P001","name":"One"}]""";
        var merged = BambuController.UpsertManifest(existing,
            new BambuPrinterRequest("2.2.2.2", "B", "y", "BL-P001", "Two"));
        Assert.Contains("\"serial\":\"A\"", merged);
        Assert.Contains("\"serial\":\"B\"", merged);
    }

    [Fact]
    public void UpsertManifest_SameSerial_Replaces()
    {
        var existing = """[{"host":"1.1.1.1","serial":"A","accessCode":"x","model":"BL-P001","name":"One"}]""";
        var merged = BambuController.UpsertManifest(existing,
            new BambuPrinterRequest("9.9.9.9", "A", "z", "BL-P001", "One-Updated"));
        Assert.Contains("9.9.9.9", merged);
        Assert.DoesNotContain("1.1.1.1", merged);
    }

    [Fact]
    public void UpsertManifest_MissingExisting_ReturnsArrayWithJustReq()
    {
        var merged = BambuController.UpsertManifest(null,
            new BambuPrinterRequest("1.1.1.1", "A", "x", "BL-P001", "One"));
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("A", doc.RootElement[0].GetProperty("serial").GetString());
    }

    [Fact]
    public void UpsertManifest_BlankExisting_ReturnsArrayWithJustReq()
    {
        var merged = BambuController.UpsertManifest("   ",
            new BambuPrinterRequest("1.1.1.1", "A", "x", "BL-P001", "One"));
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }

    private sealed class FakeRepo : IApplicationConfigRepository
    {
        public ApplicationConfig? Saved;
        public Task<ApplicationConfig?> GetByIdAsync(string id) => Task.FromResult<ApplicationConfig?>(Saved);
        public Task<List<ApplicationConfig>> GetAllAsync() => Task.FromResult(new List<ApplicationConfig>());
        public Task UpsertAsync(ApplicationConfig c) { Saved = c; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private static (BambuController controller, FakeRepo repo, IActorRef pluginRegistry)
        CreateController(ApplicationConfig? preloaded = null)
    {
        var repo = new FakeRepo { Saved = preloaded };
        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));
        return (new BambuController(repo, pluginRegistryProvider), repo, actorRef);
    }

    [Fact]
    public async Task Add_PersistsManifestEnablesAndRoutesChange()
    {
        var (controller, repo, pluginRegistry) = CreateController();

        var result = await controller.Add(new BambuPrinterRequest("192.168.1.50", "01P00A", "abc123", "BL-P001", "Living Room"));

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.Enabled);
        var manifest = JsonDocument.Parse(repo.Saved.Settings["account.manifest"]).RootElement;
        Assert.Equal("01P00A", manifest[0].GetProperty("serial").GetString());
        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "bambu" && r.Message is IntegrationConfigChanged),
            Arg.Any<IActorRef>());
    }

    [Fact]
    public async Task List_ReturnsManifestWithoutAccessCodes()
    {
        var manifest = BambuController.UpsertManifest(null,
            new BambuPrinterRequest("192.168.1.50", "01P00A", "supersecret", "BL-P001", "Living Room"));
        var preloaded = new ApplicationConfig
        {
            Id = "bambu", Name = "Bambu", Enabled = true,
            Settings = new Dictionary<string, string> { ["account.manifest"] = manifest },
        };
        var (controller, _, _) = CreateController(preloaded);

        var result = await controller.List();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("supersecret", json);
        Assert.Contains("01P00A", json);
    }

    [Fact]
    public async Task Delete_RemovesPrinterAndRoutesChange()
    {
        var manifest = BambuController.UpsertManifest(null,
            new BambuPrinterRequest("192.168.1.50", "01P00A", "abc123", "BL-P001", "Living Room"));
        var preloaded = new ApplicationConfig
        {
            Id = "bambu", Name = "Bambu", Enabled = true,
            Settings = new Dictionary<string, string> { ["account.manifest"] = manifest },
        };
        var (controller, repo, pluginRegistry) = CreateController(preloaded);

        var result = await controller.Delete("01P00A");

        Assert.IsType<OkObjectResult>(result);
        var updated = JsonDocument.Parse(repo.Saved!.Settings["account.manifest"]).RootElement;
        Assert.Equal(0, updated.GetArrayLength());
        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "bambu" && r.Message is IntegrationConfigChanged),
            Arg.Any<IActorRef>());
    }
}
