namespace Vidar.Core.Messages;

public sealed record ApplicationStatusUpdate(
    string ApplicationId,
    string Status,
    int DeviceCount,
    string? ErrorMessage = null);
