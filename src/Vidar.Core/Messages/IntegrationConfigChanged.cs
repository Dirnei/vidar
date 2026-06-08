namespace Vidar.Core.Messages;

public sealed record IntegrationConfigChanged(string IntegrationId, bool Enabled, Dictionary<string, string> Settings);
