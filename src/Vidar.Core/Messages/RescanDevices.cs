namespace Vidar.Core.Messages;

// Host → plugin: request an on-demand device rescan (e.g. from a "Rescan devices" button), so an
// integration can surface newly-available devices into discovery without any background polling.
public sealed record RescanDevices(string IntegrationId);
