namespace Vidar.Communication.Spotify;

// Derives per-accepted-device capability updates from the single global playback + device list.
// Only the active device shows playing/now-playing; volume prefers the live value, falls back to the
// persisted last-known, and is omitted entirely when neither exists (never clobbered to 0 offline).
public static class SpotifyFanOut
{
    public static IReadOnlyList<(Guid DeviceId, IReadOnlyList<(string Key, object Value)> Updates)> Build(
        SpotifyPlayback playback,
        IReadOnlyList<SpotifyDevice> devices,
        IReadOnlyDictionary<string, Guid> accepted,
        IReadOnlyDictionary<string, int> persistedVolumes)
    {
        var byId = devices.ToDictionary(d => d.Id);
        var result = new List<(Guid, IReadOnlyList<(string, object)>)>();

        foreach (var (nativeId, deviceId) in accepted)
        {
            var isActive = playback.ActiveDeviceId == nativeId;
            var updates = new List<(string, object)>
            {
                ("active", isActive),
                ("playback", isActive && playback.IsPlaying),
                ("now_playing", isActive ? playback.NowPlaying : new Dictionary<string, object>()),
            };

            int? volume = byId.TryGetValue(nativeId, out var dev) && dev.VolumePercent is int live
                ? live
                : persistedVolumes.TryGetValue(nativeId, out var stored) ? stored : (int?)null;
            if (volume is int vv) updates.Add(("volume", vv));

            result.Add((deviceId, updates));
        }
        return result;
    }
}
