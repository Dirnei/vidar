using Vidar.Communication.Dyson;
using Xunit;

namespace Vidar.Communication.Dyson.Tests;

public class DysonReconnectPolicyTests
{
    [Fact] public void Connected_DoesNotRetry() =>
        Assert.False(DysonReconnectPolicy.Next(DysonConnectOutcome.Connected).Retry);

    [Fact] public void AuthExpired_DoesNotAutoRetry() =>
        Assert.False(DysonReconnectPolicy.Next(DysonConnectOutcome.AuthExpired).Retry);

    [Fact]
    public void Transient_Retries_After15s()
    {
        var d = DysonReconnectPolicy.Next(DysonConnectOutcome.TransientFailure);
        Assert.True(d.Retry);
        Assert.Equal(TimeSpan.FromSeconds(15), d.Delay);
    }

    [Fact]
    public void RateLimited_Retries_AtLeast60s()
    {
        var d = DysonReconnectPolicy.Next(DysonConnectOutcome.RateLimited);
        Assert.True(d.Retry);
        Assert.True(d.Delay >= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void RateLimited_HonoursLongerRetryAfter()
    {
        var d = DysonReconnectPolicy.Next(DysonConnectOutcome.RateLimited, TimeSpan.FromSeconds(120));
        Assert.Equal(TimeSpan.FromSeconds(120), d.Delay);
    }
}
