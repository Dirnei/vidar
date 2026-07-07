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
using Vidar.Host.Loxone;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Loxone;

public class LoxoneControllerTests
{
    private sealed class FakeRepo : IApplicationConfigRepository
    {
        public ApplicationConfig? Saved;
        public Task<ApplicationConfig?> GetByIdAsync(string id) => Task.FromResult<ApplicationConfig?>(Saved);
        public Task<List<ApplicationConfig>> GetAllAsync() => Task.FromResult(new List<ApplicationConfig>());
        public Task UpsertAsync(ApplicationConfig c) { Saved = c; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private static (LoxoneController controller, FakeRepo repo, ILoxoneSidecar sidecar, IActorRef pluginRegistry)
        CreateController(ApplicationConfig? preloaded = null)
    {
        var repo = new FakeRepo { Saved = preloaded };
        var sidecar = Substitute.For<ILoxoneSidecar>();
        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));
        var roomMappings = Substitute.For<IRoomMappingRepository>();
        var discoveredRepo = Substitute.For<IDiscoveredDeviceRepository>();
        var deviceRepo = Substitute.For<IDeviceRepository>();
        var roomRepo = Substitute.For<IRoomRepository>();
        roomMappings.GetAllAsync().Returns(Task.FromResult(new List<RoomMapping>()));
        discoveredRepo.GetAllAsync().Returns(Task.FromResult(new List<DiscoveredDevice>()));
        deviceRepo.GetAllAsync().Returns(Task.FromResult(new List<DeviceConfiguration>()));
        roomRepo.GetAllAsync().Returns(Task.FromResult(new List<RoomConfiguration>()));
        return (new LoxoneController(sidecar, repo, pluginRegistryProvider, roomMappings, discoveredRepo, deviceRepo, roomRepo),
            repo, sidecar, actorRef);
    }

    private static List<JsonElement> ReadMiniservers(ApplicationConfig? cfg)
    {
        var json = cfg!.Settings["miniservers"];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Fact]
    public async Task AddMiniserver_PersistsEntry_EnablesConfig_AndNotifiesPlugin()
    {
        var (controller, repo, sidecar, pluginRegistry) = CreateController();
        sidecar.ProbeAsync("10.0.0.5", "admin", "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LoxoneProbeResult("504F94A0", 12, 3)));

        var result = await controller.AddMiniserver(
            new LoxoneMiniserverRequest("10.0.0.5", "admin", "secret"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("504F94A0", doc.RootElement.GetProperty("serial").GetString());
        Assert.Equal(12, doc.RootElement.GetProperty("controlCount").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("roomCount").GetInt32());

        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.Enabled);
        var entries = ReadMiniservers(repo.Saved);
        Assert.Single(entries);
        Assert.Equal("504F94A0", entries[0].GetProperty("serial").GetString());
        Assert.Equal("10.0.0.5", entries[0].GetProperty("host").GetString());
        Assert.Equal("admin", entries[0].GetProperty("user").GetString());
        Assert.Equal("secret", entries[0].GetProperty("password").GetString());

        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "loxone" && r.Message is IntegrationConfigChanged),
            Arg.Any<IActorRef>());
    }

    [Fact]
    public async Task AddMiniserver_SecondDifferentSerial_Appends()
    {
        var (controller, repo, sidecar, _) = CreateController();
        sidecar.ProbeAsync("10.0.0.5", "admin", "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LoxoneProbeResult("SERIAL-1", 1, 1)));
        await controller.AddMiniserver(new LoxoneMiniserverRequest("10.0.0.5", "admin", "secret"), default);

        sidecar.ProbeAsync("10.0.0.6", "admin2", "secret2", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LoxoneProbeResult("SERIAL-2", 2, 2)));
        await controller.AddMiniserver(new LoxoneMiniserverRequest("10.0.0.6", "admin2", "secret2"), default);

        var entries = ReadMiniservers(repo.Saved);
        Assert.Equal(2, entries.Count);
        var serials = entries.Select(e => e.GetProperty("serial").GetString()).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "SERIAL-1", "SERIAL-2" }, serials);
    }

    [Fact]
    public async Task AddMiniserver_DuplicateSerial_ReplacesInsteadOfDuplicating()
    {
        var (controller, repo, sidecar, _) = CreateController();
        sidecar.ProbeAsync("10.0.0.5", "admin", "old-pass", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LoxoneProbeResult("SERIAL-1", 1, 1)));
        await controller.AddMiniserver(new LoxoneMiniserverRequest("10.0.0.5", "admin", "old-pass"), default);

        sidecar.ProbeAsync("10.0.0.99", "admin", "new-pass", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LoxoneProbeResult("SERIAL-1", 5, 5)));
        await controller.AddMiniserver(new LoxoneMiniserverRequest("10.0.0.99", "admin", "new-pass"), default);

        var entries = ReadMiniservers(repo.Saved);
        Assert.Single(entries);
        Assert.Equal("SERIAL-1", entries[0].GetProperty("serial").GetString());
        Assert.Equal("10.0.0.99", entries[0].GetProperty("host").GetString());
        Assert.Equal("new-pass", entries[0].GetProperty("password").GetString());
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
        Assert.Equal(0, doc.RootElement.GetProperty("miniservers").GetArrayLength());
    }

    [Fact]
    public async Task Account_ReturnsConnected_WithHost_ButNeverPassword()
    {
        var manifest = JsonSerializer.Serialize(new[]
        {
            new { serial = "SERIAL-1", host = "10.0.0.5", user = "admin", password = "topsecret" },
        });
        var preloaded = new ApplicationConfig
        {
            Id = "loxone", Name = "Loxone", Enabled = true,
            Settings = new Dictionary<string, string> { ["miniservers"] = manifest },
        };
        var (controller, _, _, _) = CreateController(preloaded);

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("topsecret", json);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("connected").GetBoolean());
        var ms = doc.RootElement.GetProperty("miniservers")[0];
        Assert.Equal("SERIAL-1", ms.GetProperty("serial").GetString());
        Assert.Equal("10.0.0.5", ms.GetProperty("host").GetString());
    }

    [Fact]
    public async Task RemoveMiniserver_RemovesEntry_AndNotifiesPlugin()
    {
        var manifest = JsonSerializer.Serialize(new[]
        {
            new { serial = "SERIAL-1", host = "10.0.0.5", user = "admin", password = "pw1" },
            new { serial = "SERIAL-2", host = "10.0.0.6", user = "admin", password = "pw2" },
        });
        var preloaded = new ApplicationConfig
        {
            Id = "loxone", Name = "Loxone", Enabled = true,
            Settings = new Dictionary<string, string> { ["miniservers"] = manifest },
        };
        var (controller, repo, _, pluginRegistry) = CreateController(preloaded);

        var result = await controller.RemoveMiniserver("SERIAL-1");

        Assert.IsType<OkObjectResult>(result);
        var entries = ReadMiniservers(repo.Saved);
        Assert.Single(entries);
        Assert.Equal("SERIAL-2", entries[0].GetProperty("serial").GetString());
        pluginRegistry.Received(1).Tell(
            Arg.Is<RouteToPlugin>(r => r.PluginId == "loxone" && r.Message is IntegrationConfigChanged),
            Arg.Any<IActorRef>());
    }

    [Fact]
    public async Task RemoveMiniserver_ReturnsNotFound_WhenSerialUnknown()
    {
        var manifest = JsonSerializer.Serialize(new[]
        {
            new { serial = "SERIAL-1", host = "10.0.0.5", user = "admin", password = "pw1" },
        });
        var preloaded = new ApplicationConfig
        {
            Id = "loxone", Name = "Loxone", Enabled = true,
            Settings = new Dictionary<string, string> { ["miniservers"] = manifest },
        };
        var (controller, _, _, _) = CreateController(preloaded);

        var result = await controller.RemoveMiniserver("NOPE");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AddMiniserver_Returns401_WhenSidecarRejects()
    {
        var (controller, _, sidecar, _) = CreateController();
        sidecar.ProbeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<LoxoneProbeResult>(_ => throw new HttpRequestException("nope", null, HttpStatusCode.Unauthorized));

        var result = await controller.AddMiniserver(
            new LoxoneMiniserverRequest("10.0.0.5", "admin", "wrong"), default);

        Assert.Equal(401, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task AddMiniserver_Returns502_WhenSidecarUnreachable()
    {
        var (controller, _, sidecar, _) = CreateController();
        sidecar.ProbeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<LoxoneProbeResult>(_ => throw new HttpRequestException("connect failed"));

        var result = await controller.AddMiniserver(
            new LoxoneMiniserverRequest("10.0.0.5", "admin", "wrong"), default);

        Assert.Equal(502, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
