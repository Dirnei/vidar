namespace Vidar.Host.Api.Dto;

public sealed record DeviceCommandRequest(string CapabilityKey, object Value);
