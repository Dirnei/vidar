using System.Net;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

namespace Vidar.Communication.Shelly.Tests;

public sealed class ShellyBridgeActorTests : TestKit
{
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

    public ShellyBridgeActorTests() : base(TestConfig, "ShellyBridgeActorTests") { }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public string[] Snapshot()
        {
            lock (_requests) return [.. _requests];
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests) _requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    // Regression: a device configured at runtime (RegisterDeviceForPolling arriving after the
    // bridge started) must become commandable without a restart. Previously the Shelly bridge
    // only registered the startup batch (OnPluginRegistered) and dropped runtime registrations.
    [Fact]
    public void Command_ForDeviceRegisteredAtRuntime_ReachesDevice()
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(CreateTestProbe());
        registry.Register<DeviceTwinRegion>(CreateTestProbe());

        var handler = new RecordingHandler();
        var httpClient = new ShellyHttpClient(new HttpClient(handler));
        var bridge = Sys.ActorOf(ShellyBridgeActor.Props(httpClient));

        var deviceId = Guid.NewGuid();
        const string nativeId = "AABBCCDDEEFF";
        bridge.Tell(new RegisterDeviceForPolling(deviceId, "shelly", nativeId, "10.0.0.5", 2,
            [new CapabilityDescriptor { Key = "light", Label = "Light", Unit = UnitType.OnOff, Commandable = true }]));

        bridge.Tell(new DeviceCommand(deviceId, "shelly", nativeId, "light", true));

        AwaitAssert(
            () => Assert.Contains(handler.Snapshot(), r => r.Contains("/rpc/Light.Set") && r.Contains("on=true")),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100));
    }
}
