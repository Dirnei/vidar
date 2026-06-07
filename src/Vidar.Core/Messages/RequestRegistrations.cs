namespace Vidar.Core.Messages;

public sealed record RequestRegistrations(string CommunicationType);
public sealed record RegistrationResponse(List<RegisterDeviceForPolling> Devices);
