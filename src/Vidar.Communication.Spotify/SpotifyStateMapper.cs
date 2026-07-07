using System.Text.Json;

namespace Vidar.Communication.Spotify;

// Maps the `GET /me/player` payload to standardized capability updates. Present-fields only.
// An empty body (Spotify returns 204 when nothing is active) maps to an idle state so the
// card reliably clears.
public static class SpotifyStateMapper
{
    public static IReadOnlyList<(string CapabilityKey, object Value)> MapPlayer(string json)
    {
        var result = new List<(string, object)>();

        if (string.IsNullOrWhiteSpace(json))
        {
            result.Add(("playback", false));
            result.Add(("now_playing", new Dictionary<string, object>()));
            return result;
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            result.Add(("playback", false));
            result.Add(("now_playing", new Dictionary<string, object>()));
            return result;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.Add(("playback", false));
                result.Add(("now_playing", new Dictionary<string, object>()));
                return result;
            }

            var isPlaying = root.TryGetProperty("is_playing", out var ip) && ip.ValueKind == JsonValueKind.True;
            result.Add(("playback", isPlaying));

            if (root.TryGetProperty("device", out var dev) && dev.ValueKind == JsonValueKind.Object)
            {
                if (dev.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    result.Add(("zone", id.GetString() ?? ""));
                if (dev.TryGetProperty("volume_percent", out var vol) && vol.ValueKind == JsonValueKind.Number
                    && vol.TryGetDouble(out var v))
                    result.Add(("volume", v));
            }

            result.Add(("now_playing", BuildNowPlaying(root)));
        }

        return result;
    }

    private static Dictionary<string, object> BuildNowPlaying(JsonElement root)
    {
        var np = new Dictionary<string, object>();
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            return np;

        np["title"] = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? "" : "";

        var artists = new List<string>();
        if (item.TryGetProperty("artists", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
                if (a.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String)
                    artists.Add(an.GetString() ?? "");
        np["artist"] = string.Join(", ", artists);

        np["album"] = "";
        np["artUrl"] = "";
        if (item.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            if (album.TryGetProperty("name", out var albName) && albName.ValueKind == JsonValueKind.String)
                np["album"] = albName.GetString() ?? "";
            if (album.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array
                && imgs.GetArrayLength() > 0)
            {
                var first = imgs[0];
                if (first.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    np["artUrl"] = u.GetString() ?? "";
            }
        }

        if (root.TryGetProperty("progress_ms", out var pm) && pm.ValueKind == JsonValueKind.Number
            && pm.TryGetDouble(out var pmv))
            np["progressMs"] = pmv;
        if (item.TryGetProperty("duration_ms", out var dm) && dm.ValueKind == JsonValueKind.Number
            && dm.TryGetDouble(out var dmv))
            np["durationMs"] = dmv;

        return np;
    }
}
