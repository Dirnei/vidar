using Vidar.Core.Capabilities;

namespace Vidar.Communication.Spotify;

public static class SpotifyCapabilities
{
    public static List<CapabilityDescriptor> Build() =>
    [
        new() { Key = "playback",    Label = "Playback",    Unit = UnitType.OnOff,   Commandable = true },
        new() { Key = "track",       Label = "Track",       Unit = UnitType.Action,  Commandable = true },
        new() { Key = "volume",      Label = "Volume",      Unit = UnitType.Percent, Commandable = true, Min = 0, Max = 100 },
        new() { Key = "now_playing", Label = "Now Playing", Unit = UnitType.Text,    Commandable = false },
        new() { Key = "active",      Label = "Active",      Unit = UnitType.OnOff,   Commandable = false },
    ];
}
