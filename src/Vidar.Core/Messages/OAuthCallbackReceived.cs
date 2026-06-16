namespace Vidar.Core.Messages;

public sealed record OAuthCallbackReceived(
    string IntegrationId,
    string Code,
    string? State,
    DateTimeOffset ReceivedAt);
