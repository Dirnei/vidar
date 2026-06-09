using System.Text.Json.Serialization;

namespace Vidar.Communication.UniFi;

public sealed record UniFiCamera(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("firmwareVersion")] string? FirmwareVersion,
    [property: JsonPropertyName("host")] string? Host,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("macAddress")] string? MacAddress);

public sealed record UniFiRtspStream(
    [property: JsonPropertyName("uri")] string? Uri);
