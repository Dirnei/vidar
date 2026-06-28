using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Core.Plugins;
using Vidar.Host.Api;
using Vidar.Host.Dyson;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Dyson;

public class DysonControllerTests
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
    public async Task Account_ReturnsNotConnected_WhenNoConfig()
    {
        var repo = new FakeRepo(); // Saved is null
        var controller = new DysonController(null!, repo, null!);

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Account_ReturnsNotConnected_WhenConfigDisabled()
    {
        var repo = new FakeRepo
        {
            Saved = new ApplicationConfig
            {
                Id = "dyson", Name = "Dyson", Enabled = false,
                Settings = new Dictionary<string, string> { ["account.email"] = "me@example.com" },
            }
        };
        var controller = new DysonController(null!, repo, null!);

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Account_ReturnsConnectedWithEmailAndDeviceCount_WhenConfigured()
    {
        var manifest = System.Text.Json.JsonSerializer.Serialize(new[] { new { serial = "ABC" } });
        var repo = new FakeRepo
        {
            Saved = new ApplicationConfig
            {
                Id = "dyson", Name = "Dyson", Enabled = true,
                Settings = new Dictionary<string, string>
                {
                    ["account.email"] = "me@example.com",
                    ["account.manifest"] = manifest,
                },
            }
        };
        var controller = new DysonController(null!, repo, null!);

        var result = await controller.Account();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal("me@example.com", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("deviceCount").GetInt32());
    }

    [Fact]
    public async Task Verify_PersistsAccountManifestAndEnables()
    {
        var repo = new FakeRepo();
        var pluginRegistry = Substitute.For<IActorRef>();
        var controller = DysonControllerTestFactory.CreateWithStubbedCloud(repo,
            token: "tok",
            devices: new[] { ("X6P-EU-SKA0802A", "358K", "Bedroom", "pw") },
            pluginRegistry: pluginRegistry);

        var result = await controller.Verify(new VerifyRequest
        {
            Region = "DE", Email = "me@example.com", Password = "p", ChallengeId = "c", Otp = "123456"
        }, default);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(repo.Saved);
        Assert.True(repo.Saved!.Enabled);
        Assert.Equal("tok", repo.Saved.Settings["account.token"]);
        var manifest = JsonDocument.Parse(repo.Saved.Settings["account.manifest"]).RootElement;
        Assert.Equal("X6P-EU-SKA0802A", manifest[0].GetProperty("serial").GetString());
        Assert.Equal("358K", manifest[0].GetProperty("productType").GetString());

        // Assert discovery trigger was sent to plugin registry
        pluginRegistry.Received(1).Tell(Arg.Is<RouteToPlugin>(r => r.PluginId == "dyson" && r.Message is IntegrationConfigChanged), Arg.Any<IActorRef>());

        // Assert mqttPassword was decrypted and round-tripped correctly
        Assert.Equal("pw", manifest[0].GetProperty("mqttPassword").GetString());
    }
}

internal static class DysonControllerTestFactory
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode, string)> _responder;
        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> r) => _responder = r;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (code, body) = _responder(request);
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    public static DysonController CreateWithStubbedCloud(
        IApplicationConfigRepository repo,
        string token,
        (string serial, string productType, string name, string mqttPassword)[] devices,
        IActorRef? pluginRegistry = null)
    {
        var pluginRegistryProvider = Substitute.For<IRequiredActor<PluginRegistry>>();
        var actorRef = pluginRegistry ?? Substitute.For<IActorRef>();
        pluginRegistryProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(actorRef));

        var manifestJson = BuildManifestJson(devices);
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("verify"))
                return (HttpStatusCode.OK, $"{{\"token\":\"{token}\",\"tokenType\":\"Bearer\"}}");
            if (req.RequestUri.AbsolutePath.Contains("manifest"))
                return (HttpStatusCode.OK, manifestJson);
            return (HttpStatusCode.OK, "{}");
        });
        var cloud = new DysonCloudClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://appapi.cp.dyson.com") });

        return new DysonController(cloud, repo, pluginRegistryProvider);
    }

    private static string BuildManifestJson(
        (string serial, string productType, string name, string mqttPassword)[] devices)
    {
        var entries = devices.Select(d =>
        {
            var creds = EncryptPassword(d.mqttPassword);
            return $"{{\"Serial\":\"{d.serial}\",\"ProductType\":\"{d.productType}\"," +
                   $"\"Name\":\"{d.name}\",\"LocalCredentials\":\"{creds}\",\"variant\":null}}";
        });
        return "[" + string.Join(",", entries) + "]";
    }

    private static string EncryptPassword(string password)
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes($"{{\"apPasswordHash\":\"{password}\"}}");
        return Convert.ToBase64String(enc.TransformFinalBlock(bytes, 0, bytes.Length));
    }
}
