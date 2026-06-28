namespace Vidar.Communication.Dyson;

public enum DysonConnectOutcome { Connected, AuthExpired, RateLimited, TransientFailure }

public sealed record DysonReconnectDecision(bool Retry, TimeSpan Delay);

public static class DysonReconnectPolicy
{
    public static readonly TimeSpan TransientDelay = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RateLimitedDelay = TimeSpan.FromSeconds(60);

    public static DysonReconnectDecision Next(DysonConnectOutcome outcome, TimeSpan? retryAfter = null) => outcome switch
    {
        DysonConnectOutcome.Connected => new(false, TimeSpan.Zero),
        DysonConnectOutcome.AuthExpired => new(false, TimeSpan.Zero), // wait for re-onboard
        DysonConnectOutcome.RateLimited =>
            new(true, retryAfter is { } ra && ra > RateLimitedDelay ? ra : RateLimitedDelay),
        _ => new(true, TransientDelay),
    };
}
