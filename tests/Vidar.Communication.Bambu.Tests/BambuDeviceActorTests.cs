using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Vidar.Communication.Bambu;
using Vidar.Core.Messages;

namespace Vidar.Communication.Bambu.Tests;

public sealed class BambuDeviceActorTests : TestKit
{
    // Cluster provider + DistributedPubSub config: BambuDeviceActor resolves the DeviceTwinRegion
    // shard proxy via ActorRegistry.TryGet (works fine unregistered, as exercised below) but also
    // resolves the cluster pub-sub mediator for application-status, which requires a cluster
    // actor system to construct at all. Mirrors ShellyBridgeActorTests/EcowittBridgeActorTests.
    private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
        akka {
            actor.provider = cluster
            remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            cluster {
                seed-nodes = [""akka.tcp://BambuDeviceActorTests@127.0.0.1:2563""]
                auto-down-unreachable-after = 5s
            }
        }
    ").WithFallback(DistributedPubSub.DefaultConfig());

    public BambuDeviceActorTests() : base(TestConfig, "BambuDeviceActorTests") { }

    [Fact]
    public void CaptureSnapshot_WithNoCachedFrame_RepliesNull()
    {
        // No DeviceTwinRegion registered in the ActorRegistry: the actor must still construct
        // (via ActorRegistry.TryGet) and reply to a snapshot ask.
        var cfg = new BambuConfig("127.0.0.1", "SER1", "code", "BL-P001", "Test");
        var actor = Sys.ActorOf(BambuDeviceActor.Props(cfg, Guid.NewGuid()));

        actor.Tell(new CaptureSnapshot("SER1"));

        // Cache miss triggers a fresh, off-mailbox capture attempt (PipeTo) against a printer
        // that doesn't exist; give it enough headroom to fail (refused connection / no ffmpeg)
        // and land back on the actor within the 15s capture timeout margin.
        var reply = ExpectMsg<SnapshotResult>(TimeSpan.FromSeconds(20));
        Assert.Null(reply.Jpeg);
    }

    [Fact]
    public void DeviceCommand_ForUnknownCapability_DoesNotThrow()
    {
        var cfg = new BambuConfig("127.0.0.1", "SER2", "code", "BL-P001", "Test");
        var actor = Sys.ActorOf(BambuDeviceActor.Props(cfg, Guid.NewGuid()));

        // Should be silently ignored (BambuCommandBuilder.Build returns null for unknown keys).
        actor.Tell(new DeviceCommand(Guid.NewGuid(), "bambu", "SER2", "not_a_real_capability", 0d));

        // Follow up with a snapshot ask to prove the actor is still alive and responsive.
        // Cache miss -> fresh off-mailbox capture attempt against a nonexistent printer, same
        // headroom as CaptureSnapshot_WithNoCachedFrame_RepliesNull.
        actor.Tell(new CaptureSnapshot("SER2"));
        var reply = ExpectMsg<SnapshotResult>(TimeSpan.FromSeconds(20));
        Assert.Null(reply.Jpeg);
    }
}
