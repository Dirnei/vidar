namespace Vidar.Core.Messages;

// Published by a provider bridge on each full discovery pass: the complete set of NativeIds it
// currently knows about for a given scope (e.g. a Loxone Miniserver serial). The host (Task 7)
// diffs this against its registered devices to retire ones that vanished from the source.
public sealed record DeviceManifestSnapshot(
    string CommunicationType,
    string ScopeKey,
    IReadOnlyList<string> NativeIds);
