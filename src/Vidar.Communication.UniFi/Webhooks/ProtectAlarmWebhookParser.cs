using System.Text.Json;

namespace Vidar.Communication.UniFi.Webhooks;

public sealed record ProtectAlarmTrigger(string Device, string Key, string? Value);

public sealed record ProtectAlarmEvent(
    string AlarmName,
    DateTimeOffset Timestamp,
    List<ProtectAlarmTrigger> Triggers);

/// <summary>Parses UniFi Protect alarm-manager webhook payloads (see docs/webhooks/unifi_protect.json).</summary>
public static class ProtectAlarmWebhookParser
{
    public static ProtectAlarmEvent? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("alarm", out var alarm))
                return null;

            var name = alarm.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var timestamp = doc.RootElement.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var millis)
                ? DateTimeOffset.FromUnixTimeMilliseconds(millis)
                : DateTimeOffset.MinValue;

            var triggers = new List<ProtectAlarmTrigger>();
            if (alarm.TryGetProperty("triggers", out var trigs) && trigs.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in trigs.EnumerateArray())
                {
                    triggers.Add(new ProtectAlarmTrigger(
                        t.TryGetProperty("device", out var d) ? d.GetString() ?? "" : "",
                        t.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                        t.TryGetProperty("value", out var v) ? v.GetString() : null));
                }
            }

            return new ProtectAlarmEvent(name, timestamp, triggers);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
