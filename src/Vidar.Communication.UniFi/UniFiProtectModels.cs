using System.Text.Json.Serialization;

namespace Vidar.Communication.UniFi;

public sealed record UniFiCamera(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("modelKey")] string? ModelKey,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("mac")] string? Mac);

public sealed record UniFiRtspStream(
    [property: JsonPropertyName("uri")] string? Uri);
