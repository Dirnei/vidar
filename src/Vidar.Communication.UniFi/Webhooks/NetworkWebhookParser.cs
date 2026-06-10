using System.Text.Json;

namespace Vidar.Communication.UniFi.Webhooks;

public sealed record NetworkWebhookEvent(
    string Name,
    string Message,
    Dictionary<string, string> Parameters);

/// <summary>Parses UniFi Network event webhook payloads (see docs/webhooks/unifi_networks.json).</summary>
public static class NetworkWebhookParser
{
    public static NetworkWebhookEvent? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                return null;

            var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

            var parameters = new Dictionary<string, string>();
            if (root.TryGetProperty("parameters", out var pars) && pars.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in pars.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String)
                        parameters[p.Name] = p.Value.GetString() ?? "";
            }

            return new NetworkWebhookEvent(name.GetString()!, message, parameters);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
