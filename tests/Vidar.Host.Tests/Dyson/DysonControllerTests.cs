using System.Text.Json;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Dyson;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Dyson;

public class DysonControllerTests : TestKit
{
    private sealed class FakeRepo : IApplicationConfigRepository
    {
        public ApplicationConfig? Saved;
        public Task<ApplicationConfig?> GetByIdAsync(string id) => Task.FromResult<ApplicationConfig?>(Saved);
        public Task<List<ApplicationConfig>> GetAllAsync() => Task.FromResult(new List<ApplicationConfig>());
        public Task UpsertAsync(ApplicationConfig c) { Saved = c; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    [Fact]
    public async Task SaveDevices_PersistsDevicesJsonAndEnables()
    {
        var repo = new FakeRepo();
        var controller = DysonControllerTestFactory.Create(repo, this);

        var req = new SaveDysonDevicesRequest
        {
            Devices = new()
            {
                new SaveDysonDevice { Serial = "X6p-EU-SKA0802A", ProductType = "438", MqttPassword = "pw123", Ip = "192.168.5.157" }
            }
        };

        var result = await controller.SaveDevices(req);

        Assert.IsType<NoContentResult>(result);
        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.Enabled);
        Assert.Equal("dyson", repo.Saved.Id);
        var devices = JsonDocument.Parse(repo.Saved.Settings["devices"]).RootElement;
        Assert.Equal("X6p-EU-SKA0802A", devices[0].GetProperty("serial").GetString());
        Assert.Equal("438", devices[0].GetProperty("productType").GetString());
        Assert.Equal("pw123", devices[0].GetProperty("mqttPassword").GetString());
        Assert.Equal("192.168.5.157", devices[0].GetProperty("ip").GetString());
    }
}

internal static class DysonControllerTestFactory
{
    public static DysonController Create(IApplicationConfigRepository repo, TestKit kit)
    {
        var pluginRegistry = Substitute.For<IRequiredActor<PluginRegistry>>();
        pluginRegistry.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(kit.TestActor));

        // DysonCloudClient requires an HttpClient; it won't be called in SaveDevices.
        var cloud = new DysonCloudClient(new HttpClient());

        return new DysonController(cloud, repo, pluginRegistry);
    }
}
