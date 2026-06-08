namespace Vidar.Communication.UniFi;

public sealed record UniFiConfig(string Host, string ApiKey, string SiteId, int PollIntervalSeconds);
