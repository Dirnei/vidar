using Vidar.Core.Model;

namespace Vidar.Core.Messages;

public sealed record ApplicationStatusUpdate(
    string ApplicationId,
    string Status,
    int DeviceCount,
    string? ErrorMessage = null,
    // The plugin declares whether it is a provider or a consumer when it announces itself.
    // The host relays this — it never classifies plugins itself. Defaults to Provider so
    // existing publishers (and per-device transport statuses) need no change.
    ApplicationType Type = ApplicationType.Provider);
