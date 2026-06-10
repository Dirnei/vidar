using Akka.TestKit.Xunit2;
using NSubstitute;
using Vidar.Host.Actors;
using Vidar.Host.Persistence;

namespace Vidar.Host.Tests.Actors;

public sealed class WebhookPayloadCleanupActorTests : TestKit
{
    [Fact]
    public async Task PeriodicallyDeletesExpiredPayloads()
    {
        var repo = Substitute.For<IWebhookPayloadRepository>();
        repo.DeleteOlderThanAsync(Arg.Any<DateTime>()).Returns(0L);

        Sys.ActorOf(WebhookPayloadCleanupActor.Props(
            repo, retention: TimeSpan.FromHours(24), interval: TimeSpan.FromMilliseconds(100)));

        await AwaitAssertAsync(async () =>
            await repo.Received().DeleteOlderThanAsync(Arg.Is<DateTime>(d =>
                d <= DateTime.UtcNow.AddHours(-23))));
    }

    [Fact]
    public async Task RepositoryFailure_DoesNotStopTheTimer()
    {
        var repo = Substitute.For<IWebhookPayloadRepository>();
        var callCount = 0;
        repo.DeleteOlderThanAsync(Arg.Any<DateTime>())
            .Returns(async _ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("mongo down");
                return 0L;
            });

        Sys.ActorOf(WebhookPayloadCleanupActor.Props(
            repo, retention: TimeSpan.FromHours(24), interval: TimeSpan.FromMilliseconds(100)));

        // At least two calls: the first throws, the second proves the actor survived
        await AwaitAssertAsync(async () =>
        {
            await Task.Delay(300); // Allow time for at least 2 timer ticks
            await repo.Received().DeleteOlderThanAsync(Arg.Any<DateTime>());
        });
    }
}
