namespace Vidar.Core.Messages;

public sealed record OAuthCallbackReceived(
    string IntegrationId,
    string Code,
    string? State,
    DateTimeOffset ReceivedAt,
    // The exact redirect URI the host used for this flow, resolved live from the incoming request
    // origin. The provider's token exchange must echo it back byte-for-byte, so the host is the
    // single source of truth — plugins no longer reconstruct it from a configured base URL.
    string RedirectUri);
