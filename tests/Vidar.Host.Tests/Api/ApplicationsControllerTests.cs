using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Actors;
using Vidar.Host.Api;
using Vidar.Host.Api.Dto;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Api;

public sealed class ApplicationsControllerTests : TestKit
{
    // ApplicationStatusActor.PreStart subscribes via DistributedPubSub, which requires the
    // cluster actor provider to be loaded even though we never actually join a cluster here.
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://test@127.0.0.1:2552""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    private sealed class FakeRepo : IApplicationConfigRepository
    {
        public ApplicationConfig? Saved;
        public Task<ApplicationConfig?> GetByIdAsync(string id) => Task.FromResult(Saved);
        public Task<List<ApplicationConfig>> GetAllAsync() => Task.FromResult(Saved is null ? new List<ApplicationConfig>() : new List<ApplicationConfig> { Saved });
        public Task UpsertAsync(ApplicationConfig c) { Saved = c; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private readonly FakeRepo _repo = new();
    private readonly ApplicationsController _sut;

    public ApplicationsControllerTests() : base(TestConfig, "ApplicationsControllerTests")
    {
        var statusActor = Sys.ActorOf(ApplicationStatusActor.Props());
        var statusActorProvider = Substitute.For<IRequiredActor<ApplicationStatusActor>>();
        statusActorProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(statusActor));

        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TestActor));

        _sut = new ApplicationsController(
            _repo,
            statusActorProvider,
            pluginRegistryProvider,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ApplicationsController>>());
    }

    [Fact]
    public async Task GetById_RedactsSecretSetting()
    {
        _repo.Saved = new ApplicationConfig
        {
            Id = "zigbee2mqtt",
            Name = "Zigbee2MQTT",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["mqttPassword"] = "supersecret",
                ["baseTopic"] = "zigbee2mqtt",
            },
        };

        var result = await _sut.GetById("zigbee2mqtt");

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApplicationResponse>(ok.Value);
        Assert.Equal(SettingsSecrets.RedactedSentinel, response.Settings["mqttPassword"]);
        Assert.Equal("zigbee2mqtt", response.Settings["baseTopic"]);
    }

    [Fact]
    public async Task Update_WithSentinel_PreservesStoredSecret()
    {
        _repo.Saved = new ApplicationConfig
        {
            Id = "zigbee2mqtt",
            Name = "Zigbee2MQTT",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["mqttPassword"] = "realsecret",
                ["baseTopic"] = "old-topic",
            },
        };

        var request = new UpdateApplicationRequest
        {
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["mqttPassword"] = SettingsSecrets.RedactedSentinel,
                ["baseTopic"] = "new-topic",
            },
        };

        var result = await _sut.Update("zigbee2mqtt", request);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("realsecret", _repo.Saved!.Settings["mqttPassword"]);
        Assert.Equal("new-topic", _repo.Saved.Settings["baseTopic"]);
    }
}
