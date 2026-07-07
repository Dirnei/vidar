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

/// <summary>
/// Tests for DiscoveryManagerActor's handling of DeviceManifestSnapshot: pruning discovered
/// devices absent from a scoped manifest and marking accepted (configured) devices offline —
/// scoped so a partial/foreign manifest never touches devices outside its (CommunicationType,
/// ScopeKey) authority.
/// </summary>
public sealed class DiscoveryManagerRetireTests : TestKit
{
    // DiscoveryManagerActor.PreStart subscribes to DistributedPubSub topics, which requires a
    // cluster-provider actor system (same config used by DeviceTwinActorTests / PluginRegistryActorTests).
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

    public DiscoveryManagerRetireTests() : base(TestConfig, "DiscoveryManagerRetireTests")
    {
    }

    private static DiscoveredDevice Discovered(string communicationType, string nativeId) => new()
    {
        Id = Guid.NewGuid(),
        CommunicationType = communicationType,
        NativeId = nativeId,
        Capabilities = new List<CapabilityDescriptor>(),
        Metadata = new Dictionary<string, string>(),
        DiscoveredAt = DateTime.UtcNow
    };

    private static DeviceConfiguration Accepted(string communicationType, string nativeId) => new()
    {
        Id = Guid.NewGuid(),
        Name = nativeId,
        CommunicationType = communicationType,
        NativeId = nativeId,
        Capabilities = new List<CapabilityDescriptor>(),
        Settings = new Dictionary<string, string>()
    };

    [Fact]
    public async Task Snapshot_prunes_absent_discovered_devices_in_scope_only()
    {
        // Arrange
        var discoveredAaaU1 = Discovered("loxone", "AAA/u1");
        var discoveredAaaU2 = Discovered("loxone", "AAA/u2");
        var discoveredBbbU9 = Discovered("loxone", "BBB/u9");
        var discoveredShellyX = Discovered("shelly", "x");

        var discoveredRepo = Substitute.For<IDiscoveredDeviceRepository>();
        discoveredRepo.GetAllAsync().Returns(new List<DiscoveredDevice>
        {
            discoveredAaaU1, discoveredAaaU2, discoveredBbbU9, discoveredShellyX
        });

        var acceptedAaaU2 = Accepted("loxone", "AAA/u2");
        var deviceRepo = Substitute.For<IDeviceRepository>();
        deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration> { acceptedAaaU2 });

        var shardProxyProbe = CreateTestProbe();

        var actor = Sys.ActorOf(DiscoveryManagerActor.Props(discoveredRepo, deviceRepo, shardProxyProbe));

        // Act — manifest says only AAA/u1 is present for (loxone, AAA)
        actor.Tell(new DeviceManifestSnapshot("loxone", "AAA", new List<string> { "AAA/u1" }));

        // Assert — AAA/u2 (discovered) removed
        await AwaitAssertAsync(() => discoveredRepo.Received(1).DeleteAsync(discoveredAaaU2.Id));

        // AAA/u2 (accepted) marked offline via the shard proxy
        shardProxyProbe.ExpectMsg<DeviceOffline>(msg => msg.DeviceId == acceptedAaaU2.Id, TimeSpan.FromSeconds(3));

        // AAA/u1 kept — never deleted
        await discoveredRepo.DidNotReceive().DeleteAsync(discoveredAaaU1.Id);

        // Different scope (BBB) untouched
        await discoveredRepo.DidNotReceive().DeleteAsync(discoveredBbbU9.Id);

        // Different communication type (shelly) untouched
        await discoveredRepo.DidNotReceive().DeleteAsync(discoveredShellyX.Id);

        // No further offline messages beyond the one expected
        shardProxyProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
