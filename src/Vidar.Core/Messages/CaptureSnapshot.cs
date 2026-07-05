namespace Vidar.Core.Messages;

/// <summary>Ask a communication plugin to return the latest camera frame for a device.</summary>
public sealed record CaptureSnapshot(string NativeId);

public sealed record SnapshotResult(byte[]? Jpeg);
