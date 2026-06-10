using Akka.Actor;
using Akka.Event;
using Vidar.Host.Persistence;

namespace Vidar.Host.Actors;

/// <summary>Deletes webhook payloads from GridFS once they exceed the retention window.</summary>
public sealed class WebhookPayloadCleanupActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IWebhookPayloadRepository _payloads;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _interval;

    private sealed class CleanupTick { public static readonly CleanupTick Instance = new(); }

    public static Props Props(IWebhookPayloadRepository payloads, TimeSpan retention, TimeSpan interval) =>
        Akka.Actor.Props.Create(() => new WebhookPayloadCleanupActor(payloads, retention, interval));

    public WebhookPayloadCleanupActor(IWebhookPayloadRepository payloads, TimeSpan retention, TimeSpan interval)
    {
        _payloads = payloads;
        _retention = retention;
        _interval = interval;

        ReceiveAsync<CleanupTick>(async _ =>
        {
            try
            {
                var deleted = await _payloads.DeleteOlderThanAsync(DateTime.UtcNow - _retention);
                if (deleted > 0)
                    _log.Info("Deleted {Count} expired webhook payloads", deleted);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Webhook payload cleanup failed");
            }
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        Timers.StartPeriodicTimer("cleanup", CleanupTick.Instance, _interval, _interval);
    }
}
