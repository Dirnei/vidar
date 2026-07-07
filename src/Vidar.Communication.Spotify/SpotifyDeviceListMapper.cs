using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Maps `GET /me/player/devices` to a `zones` composite the SpotifyPlayerCard renders as a
// picker. Device ids are opaque strings (can't ride CapabilityOption, whose Value is a double),
// so zones live in device state instead.
public static class SpotifyDeviceListMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapDevices(string json)
    {
        var zones = new List<Dictionary<string, object>>();
        string? activeId = null;

        if (!string.IsNullOrWhiteSpace(json))
        {
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(json); } catch (JsonException) { }
            if (doc is not null)
            {
                using (doc)
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("devices", out var arr)
                        && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in arr.EnumerateArray())
                        {
                            if (d.ValueKind != JsonValueKind.Object) continue;
                            var id = d.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                                ? idEl.GetString() : null;
                            if (string.IsNullOrEmpty(id)) continue;
                            var name = d.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                                ? nEl.GetString() ?? id : id;
                            var active = d.TryGetProperty("is_active", out var aEl) && aEl.ValueKind == JsonValueKind.True;
                            zones.Add(new Dictionary<string, object> { ["id"] = id, ["name"] = name, ["active"] = active });
                            if (active) activeId = id;
                        }
                    }
                }
            }
        }

        var result = new List<(string, object)> { ("zones", zones) };
        if (activeId is not null) result.Add(("zone", activeId));
        return result;
    }
}
