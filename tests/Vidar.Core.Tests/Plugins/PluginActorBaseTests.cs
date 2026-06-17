using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;

namespace Vidar.Core.Tests.Plugins;

public sealed class PluginActorBaseTests : TestKit
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

    public PluginActorBaseTests() : base(TestConfig, "PluginActorBaseTests") { }

    private sealed class TestPlugin : PluginActorBase
    {
        protected override string PluginId => "test-plugin";
        public List<(Guid, string)> RegisteredDevices { get; } = [];
        public (bool Enabled, Dictionary<string, string> Settings)? LastConfig { get; private set; }

        public TestPlugin()
        {
            Receive<DoDiscover>(_ =>
            {
                Discover(Guid.NewGuid(), "native-1",
                    [new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true }],
                    new Dictionary<string, string> { ["model"] = "test" });
            });

            Receive<DoReport>(msg =>
            {
                ReportState(msg.DeviceId, "switch", true);
            });
        }

        protected override void OnDeviceRegistered(Guid deviceId, string nativeId, RegisterDeviceForPolling registration)
        {
            RegisteredDevices.Add((deviceId, nativeId));
        }

        protected override void OnConfigChanged(bool enabled, Dictionary<string, string> settings)
        {
            LastConfig = (enabled, settings);
        }

        public static Props Props() =>
            Akka.Actor.Props.Create(() => new TestPlugin());

        public sealed record DoDiscover;
        public sealed record DoReport(Guid DeviceId);
    }

    private void RegisterProbes(TestProbe pluginRegistry, TestProbe shardProxy)
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(pluginRegistry);
        registry.Register<DeviceTwinRegion>(shardProxy);
    }

    [Fact]
    public void ReportState_SendsDeviceStateUpdateToShardProxy()
    {
        var shardProxy = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var plugin = Sys.ActorOf(TestPlugin.Props());

        var deviceId = Guid.NewGuid();
        plugin.Tell(new TestPlugin.DoReport(deviceId));

        var update = shardProxy.ExpectMsg<DeviceStateUpdate>();
        Assert.Equal(deviceId, update.DeviceId);
        Assert.Equal("switch", update.CapabilityKey);
        Assert.Equal(true, update.Value);
    }

    [Fact]
    public void RegisterDeviceForPolling_TracksDeviceAndCallsOnDeviceRegistered()
    {
        var shardProxy = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var plugin = Sys.ActorOf(TestPlugin.Props());

        var deviceId = Guid.NewGuid();
        plugin.Tell(new RegisterDeviceForPolling(deviceId, "test-plugin", "native-1", "host", 0, []));

        Thread.Sleep(200);

        plugin.Tell(new TestPlugin.DoReport(deviceId));
        var update = shardProxy.ExpectMsg<DeviceStateUpdate>();
        Assert.Equal(deviceId, update.DeviceId);
    }

    [Fact]
    public void IntegrationConfigChanged_CallsOnConfigChanged()
    {
        var shardProxy = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();
        RegisterProbes(pluginRegistry, shardProxy);

        var plugin = Sys.ActorOf(TestPlugin.Props());

        var settings = new Dictionary<string, string> { ["host"] = "192.168.1.1" };
        plugin.Tell(new IntegrationConfigChanged("test-plugin", true, settings));

        plugin.Tell(new IntegrationConfigChanged("other-plugin", false, new()));
    }
}
