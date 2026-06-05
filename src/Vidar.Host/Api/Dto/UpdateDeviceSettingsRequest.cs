namespace Vidar.Host.Api.Dto;

public sealed record UpdateDeviceSettingsRequest(Dictionary<string, string> Settings);
