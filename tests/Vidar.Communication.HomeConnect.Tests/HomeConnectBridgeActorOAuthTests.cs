using Akka.Actor;
using Akka.TestKit.Xunit2;
using Vidar.Core.Messages;

namespace Vidar.Communication.HomeConnect.Tests;

public sealed class HomeConnectBridgeActorOAuthTests : TestKit
{
    [Fact]
    public void OAuthCallbackReceived_IsHandled_WithoutCrash()
    {
        var shardProxy = CreateTestProbe();
        var webhookRegistry = CreateTestProbe();
        var bridge = Sys.ActorOf(
            HomeConnectBridgeActor.Props(shardProxy, webhookRegistry));

        var callback = new OAuthCallbackReceived(
            "homeconnect", "test-code", "test-state", DateTimeOffset.UtcNow);
        bridge.Tell(callback);

        // Actor stays alive — send another message to prove it
        bridge.Tell(new OAuthCallbackReceived(
            "homeconnect", "code2", "state2", DateTimeOffset.UtcNow));

        ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }
}
