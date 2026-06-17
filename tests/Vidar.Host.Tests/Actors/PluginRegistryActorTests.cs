using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Capabilities;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Actors;

public sealed class PluginRegistryActorTests : TestKit
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

    private readonly IDeviceRepository _deviceRepo = Substitute.For<IDeviceRepository>();
    private readonly IApplicationConfigRepository _appRepo = Substitute.For<IApplicationConfigRepository>();

    public PluginRegistryActorTests() : base(TestConfig, "PluginRegistryActorTests") { }

    private IActorRef CreateActor() =>
        Sys.ActorOf(PluginRegistryActor.Props(_deviceRepo, _appRepo));

    [Fact]
    public async Task RegisterPlugin_RespondsWithConfigAndRegistrations()
    {
        var devices = new List<DeviceConfiguration>
        {
            new()
            {
                Id = Guid.NewGuid(), Name = "Switch1", CommunicationType = "shelly",
                NativeId = "shelly1", Capabilities = [new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true }],
                Settings = new Dictionary<string, string> { ["host"] = "192.168.1.10", ["generation"] = "2" }
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Z2M Sensor", CommunicationType = "zigbee2mqtt",
                NativeId = "sensor1", Capabilities = [new CapabilityDescriptor { Key = "temperature", Label = "Temperature", Unit = UnitType.Celsius }]
            }
        };
        _deviceRepo.GetAllAsync().Returns(devices);
        _appRepo.GetByIdAsync("shelly").Returns(new ApplicationConfig
        {
            Id = "shelly", Name = "Shelly", Enabled = true,
            Settings = new Dictionary<string, string> { ["pollInterval"] = "10" }
        });

        var registry = CreateActor();
        registry.Tell(new RegisterPlugin("shelly", TestActor));

        var response = ExpectMsg<PluginRegistered>(TimeSpan.FromSeconds(5));
        Assert.Equal("shelly", response.PluginId);
        Assert.True(response.Enabled);
        Assert.Equal("10", response.Settings["pollInterval"]);
        Assert.Single(response.Registrations);
        Assert.Equal("shelly1", response.Registrations[0].NativeId);
    }

    [Fact]
    public async Task RegisterPlugin_NoConfig_RespondsWithDisabledAndEmptySettings()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        _appRepo.GetByIdAsync("newplugin").Returns((ApplicationConfig?)null);

        var registry = CreateActor();
        registry.Tell(new RegisterPlugin("newplugin", TestActor));

        var response = ExpectMsg<PluginRegistered>(TimeSpan.FromSeconds(5));
        Assert.Equal("newplugin", response.PluginId);
        Assert.False(response.Enabled);
        Assert.Empty(response.Settings);
        Assert.Empty(response.Registrations);
    }

    [Fact]
    public void RouteToPlugin_ForwardsToRegisteredPlugin()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        _appRepo.GetByIdAsync("shelly").Returns((ApplicationConfig?)null);

        var registry = CreateActor();
        var pluginProbe = CreateTestProbe();
        registry.Tell(new RegisterPlugin("shelly", pluginProbe));
        pluginProbe.ExpectMsg<PluginRegistered>();

        var command = new DeviceCommand(Guid.NewGuid(), "shelly", "device1", "switch", true);
        registry.Tell(new RouteToPlugin("shelly", command));

        pluginProbe.ExpectMsg<DeviceCommand>(msg => msg.NativeId == "device1");
    }

    [Fact]
    public void RouteToPlugin_UnknownPlugin_DoesNotCrash()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());

        var registry = CreateActor();
        registry.Tell(new RouteToPlugin("nonexistent", "hello"));

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void TerminatedPlugin_IsRemovedFromRegistry()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        _appRepo.GetByIdAsync("shelly").Returns((ApplicationConfig?)null);

        var registry = CreateActor();
        var pluginProbe = CreateTestProbe();
        registry.Tell(new RegisterPlugin("shelly", pluginProbe));
        pluginProbe.ExpectMsg<PluginRegistered>();

        Sys.Stop(pluginProbe);
        Thread.Sleep(500);

        // After termination, routing should silently drop
        registry.Tell(new RouteToPlugin("shelly", "hello"));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void IntegrationConfigChanged_ForwardedToRegisteredPlugin()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        _appRepo.GetByIdAsync("shelly").Returns((ApplicationConfig?)null);

        var registry = CreateActor();
        var pluginProbe = CreateTestProbe();
        registry.Tell(new RegisterPlugin("shelly", pluginProbe));
        pluginProbe.ExpectMsg<PluginRegistered>();

        var configChanged = new IntegrationConfigChanged("shelly", true, new Dictionary<string, string> { ["key"] = "val" });
        registry.Tell(new RouteToPlugin("shelly", configChanged));

        pluginProbe.ExpectMsg<IntegrationConfigChanged>(msg => msg.IntegrationId == "shelly");
    }

    [Fact]
    public void RegisterDeviceForPolling_ForwardedToRegisteredPlugin()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        _appRepo.GetByIdAsync("shelly").Returns((ApplicationConfig?)null);

        var registry = CreateActor();
        var pluginProbe = CreateTestProbe();
        registry.Tell(new RegisterPlugin("shelly", pluginProbe));
        pluginProbe.ExpectMsg<PluginRegistered>();

        var reg = new RegisterDeviceForPolling(Guid.NewGuid(), "shelly", "dev1", "192.168.1.5", 2, [new CapabilityDescriptor { Key = "switch", Label = "Switch", Unit = UnitType.OnOff, Commandable = true }]);
        registry.Tell(new RouteToPlugin("shelly", reg));

        pluginProbe.ExpectMsg<RegisterDeviceForPolling>(msg => msg.NativeId == "dev1");
    }

    [Fact]
    public async Task RegisterPlugin_OverwritesPreviousRegistration()
    {
        _deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        _appRepo.GetByIdAsync("shelly").Returns((ApplicationConfig?)null);

        var registry = CreateActor();
        var probe1 = CreateTestProbe();
        var probe2 = CreateTestProbe();

        registry.Tell(new RegisterPlugin("shelly", probe1));
        probe1.ExpectMsg<PluginRegistered>();

        registry.Tell(new RegisterPlugin("shelly", probe2));
        probe2.ExpectMsg<PluginRegistered>();

        // Route should go to probe2 now
        registry.Tell(new RouteToPlugin("shelly", "test"));
        probe2.ExpectMsg<string>(s => s == "test");
        probe1.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
