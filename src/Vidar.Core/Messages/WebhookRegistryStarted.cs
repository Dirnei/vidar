namespace Vidar.Core.Messages;

/// <summary>
/// Published via DistributedPubSub when the webhook registry singleton starts.
/// Bridges subscribe and re-register their routes in response.
/// </summary>
public sealed class WebhookRegistryStarted
{
    public static readonly WebhookRegistryStarted Instance = new();
    private WebhookRegistryStarted() { }
}
