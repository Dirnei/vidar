using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;
using Vidar.Core.Plugins;
using Vidar.Core.Sharding;
using Vidar.Core.Webhooks;

namespace Vidar.Communication.HomeConnect.Tests;

public sealed class HomeConnectBridgeActorOAuthTests : TestKit
{
    [Fact]
    public void OAuthCallbackReceived_IsHandled_WithoutCrash()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();
        var pluginRegistry = CreateTestProbe();

        var registry = ActorRegistry.For(Sys);
        registry.Register<PluginRegistry>(pluginRegistry);
        registry.Register<DeviceTwinRegion>(shardProxy);
        registry.Register<WebhookRegistry>(webhookRegistry);

        var bridge = Sys.ActorOf(
            HomeConnectBridgeActor.Props());

        var callback = new OAuthCallbackReceived(
            "homeconnect", "test-code", "test-state", DateTimeOffset.UtcNow);
        bridge.Tell(callback);

        bridge.Tell(new OAuthCallbackReceived(
            "homeconnect", "code2", "state2", DateTimeOffset.UtcNow));

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
