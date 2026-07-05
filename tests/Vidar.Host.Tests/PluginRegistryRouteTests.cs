using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Core.Messages;
using Vidar.Core.Model;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;
using Xunit;

namespace Vidar.Host.Tests;

public class PluginRegistryRouteTests : TestKit
{
    [Fact]
    public async Task RouteToPlugin_ForwardsSender_SoAskGetsReply()
    {
        var deviceRepo = Substitute.For<IDeviceRepository>();
        var appRepo = Substitute.For<IApplicationConfigRepository>();
        deviceRepo.GetAllAsync().Returns(new List<DeviceConfiguration>());
        appRepo.GetByIdAsync("bambu").Returns((ApplicationConfig?)null);

        // A stub "plugin" that replies to Sender when it gets a CaptureSnapshot.
        var plugin = Sys.ActorOf(akka => akka.Receive<CaptureSnapshot>((m, ctx) =>
            ctx.Sender.Tell(new SnapshotResult(new byte[] { 1, 2, 3 }))));

        var registry = Sys.ActorOf(PluginRegistryActor.Props(deviceRepo, appRepo));
        registry.Tell(new RegisterPlugin("bambu", plugin));

        var reply = await registry.Ask<SnapshotResult>(
            new RouteToPlugin("bambu", new CaptureSnapshot("SER1")), TimeSpan.FromSeconds(3));
        Assert.Equal(3, reply.Jpeg!.Length);
    }
}
