using Vidar.Core.Capabilities;

namespace Vidar.Communication.Spotify;

public static class SpotifyCapabilities
{
    // Per-speaker device: controls THIS Connect device; `active` says whether it is the current
    // playback target ("Play here" when not).
    public static List<CapabilityDescriptor> Build() =>
    [
        new() { Key = "playback",    Label = "Playback",    Unit = UnitType.OnOff,   Commandable = true },
        new() { Key = "track",       Label = "Track",       Unit = UnitType.Action,  Commandable = true },
        new() { Key = "volume",      Label = "Volume",      Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 },
        new() { Key = "now_playing", Label = "Now Playing", Unit = UnitType.Text,    Commandable = false },
        new() { Key = "active",      Label = "Active",      Unit = UnitType.OnOff,   Commandable = false },
    ];

    // Central "Spotify Player" device: controls whatever is currently playing and picks the target
    // device via `zone` (transfer the stream). Populated with the full device list for the dropdown.
    public static List<CapabilityDescriptor> BuildPlayer() =>
    [
        new() { Key = "playback",    Label = "Playback",    Unit = UnitType.OnOff,   Commandable = true },
        new() { Key = "track",       Label = "Track",       Unit = UnitType.Action,  Commandable = true },
        new() { Key = "volume",      Label = "Volume",      Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 },
        new() { Key = "now_playing", Label = "Now Playing", Unit = UnitType.Text,    Commandable = false },
        new() { Key = "zone",        Label = "Zone",        Unit = UnitType.Text,    Commandable = true },
    ];
}
