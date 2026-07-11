using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Maps `GET /me/player/devices` to a `zones` composite the SpotifyPlayerCard renders as a
// picker. Device ids are opaque strings (can't ride CapabilityOption, whose Value is a double),
// so zones live in device state instead.
public static class SpotifyDeviceListMapper
{
    // Parses GET /me/player/devices into a flat SpotifyDevice list used for discovery + per-device
    // state fan-out. Tolerant of empty/garbage payloads (returns empty).
    public static IReadOnlyList<SpotifyDevice> Parse(string json)
    {
        var result = new List<SpotifyDevice>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { return result; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("devices", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var d in arr.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object) continue;
                var id = d.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                var name = d.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString() ?? id : id;
                var active = d.TryGetProperty("is_active", out var aEl) && aEl.ValueKind == JsonValueKind.True;
                int? vol = d.TryGetProperty("volume_percent", out var vEl)
                    && vEl.ValueKind == JsonValueKind.Number && vEl.TryGetInt32(out var v) ? v : null;
                result.Add(new SpotifyDevice(id, name, active, vol));
            }
        }
        return result;
    }
}
