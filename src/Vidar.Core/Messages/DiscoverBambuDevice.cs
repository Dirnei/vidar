namespace Vidar.Core.Messages;

/// <summary>
/// Ask the Bambu worker to probe a printer at <paramref name="Host"/> using
/// <paramref name="AccessCode"/>, read its serial off the local MQTT stream, and publish it as a
/// discovered device. Mirrors <see cref="DiscoverShellyDevice"/> — a local, add-by-IP discovery.
/// </summary>
public sealed record DiscoverBambuDevice(string Host, string AccessCode);
