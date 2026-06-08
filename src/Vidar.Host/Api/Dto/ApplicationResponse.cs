namespace Vidar.Host.Api.Dto;

public sealed record ApplicationResponse(
    string Id,
    string Name,
    string Type,
    bool Enabled,
    string Status,
    int DeviceCount,
    Dictionary<string, string> Settings,
    string? ErrorMessage);
