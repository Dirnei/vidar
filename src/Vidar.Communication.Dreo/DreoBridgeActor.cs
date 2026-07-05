using System.Text.Json;
using Vidar.Core.Plugins;

namespace Vidar.Communication.Dreo;

public sealed partial class DreoBridgeActor : PluginActorBase
{
    protected override string PluginId => "dreo";

    /// <summary>
    /// Parses <c>account.manifest</c> into (credential, display name) pairs.
    /// Malformed entries (missing serial) are skipped rather than throwing.
    /// </summary>
    public static List<(DreoDeviceCredential Cred, string Name)> ParseManifest(
        IReadOnlyDictionary<string, string> settings)
    {
        var result = new List<(DreoDeviceCredential, string)>();
        if (!settings.TryGetValue("account.manifest", out var json) || string.IsNullOrWhiteSpace(json))
            return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return result; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (!e.TryGetProperty("serial", out var serialEl) || serialEl.ValueKind != JsonValueKind.String)
                    continue;
                var serial = serialEl.GetString();
                if (string.IsNullOrWhiteSpace(serial)) continue;

                var model = e.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? mEl.GetString() ?? "" : "";
                var name = e.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString() ?? serial : serial;

                result.Add((new DreoDeviceCredential(serial, model), name));
            }
        }
        return result;
    }
}
