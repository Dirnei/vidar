using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vidar.Host.Api;

// Shared secret-redaction logic for the generic applications API. Provider settings dictionaries
// (Loxone `miniservers` JSON, Dreo `account.token`, generic-form `mqttPassword`/`apiKey`/etc.)
// contain cleartext credentials. GET must never leak them; PUT must still allow the frontend's
// generic settings form to round-trip non-secret edits without clobbering a stored secret with
// the redacted placeholder it echoed back.
public static class SettingsSecrets
{
    public const string RedactedSentinel = "__REDACTED__";

    private static readonly string[] SecretKeyMarkers =
    [
        "password", "passwd", "pwd", "secret", "token", "apikey", "accesscode", "rscpkey", "credential",
    ];

    private static bool IsSecretKey(string key) =>
        SecretKeyMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns a new dictionary with secret-bearing values replaced by <see cref="RedactedSentinel"/>.
    /// Top-level keys are matched directly; values that parse as JSON objects/arrays are deep-redacted
    /// (e.g. Loxone's `miniservers` array, whose entries carry a nested `password`). Never mutates
    /// the input.
    /// </summary>
    public static Dictionary<string, string> Redact(IReadOnlyDictionary<string, string> settings)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in settings)
        {
            if (IsSecretKey(key))
            {
                result[key] = RedactedSentinel;
                continue;
            }

            result[key] = RedactJsonIfApplicable(value);
        }

        return result;
    }

    /// <summary>
    /// Returns a new dictionary where any incoming entry whose value is the redacted sentinel is
    /// replaced by the corresponding value from <paramref name="existing"/>; if there is no prior
    /// value for that key, the entry is dropped rather than persisting the literal sentinel. All
    /// other incoming entries pass through unchanged.
    /// </summary>
    public static Dictionary<string, string> PreserveRedacted(
        IReadOnlyDictionary<string, string> incoming, IReadOnlyDictionary<string, string>? existing)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in incoming)
        {
            if (value == RedactedSentinel)
            {
                if (existing is not null && existing.TryGetValue(key, out var priorValue))
                    result[key] = priorValue;
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static string RedactJsonIfApplicable(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return value;
        }

        if (node is not (JsonObject or JsonArray))
            return value;

        RedactNode(node);
        return node.ToJsonString();
    }

    private static void RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (IsSecretKey(key) && child is JsonValue childValue && childValue.TryGetValue<string>(out _))
                        obj[key] = RedactedSentinel;
                    else
                        RedactNode(child);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    RedactNode(item);
                break;
        }
    }
}
