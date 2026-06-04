using Vidar.Core.Capabilities;

namespace Vidar.Host.Api.Dto;

public sealed record DeviceCommandRequest(CapabilityType Capability, object Value);
