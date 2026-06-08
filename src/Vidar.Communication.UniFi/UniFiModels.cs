using System.Text.Json.Serialization;

namespace Vidar.Communication.UniFi;

// Paginated response wrapper
public sealed record PagedResponse<T>(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("data")] List<T> Data);

// Sites
public sealed record UniFiSite(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("internalReference")] string? InternalReference,
    [property: JsonPropertyName("name")] string? Name);

// Devices
public sealed record UniFiNetworkDevice(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("macAddress")] string? MacAddress,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("firmwareVersion")] string? FirmwareVersion,
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("features")] List<string>? Features,
    [property: JsonPropertyName("interfaces")] List<string>? Interfaces,
    [property: JsonPropertyName("supported")] bool? Supported);

// Device statistics
public sealed record UniFiDeviceStats(
    [property: JsonPropertyName("uptimeSec")] long? UptimeSec,
    [property: JsonPropertyName("cpuUtilizationPct")] double? CpuUtilizationPct,
    [property: JsonPropertyName("memoryUtilizationPct")] double? MemoryUtilizationPct,
    [property: JsonPropertyName("loadAverage1Min")] double? LoadAverage1Min,
    [property: JsonPropertyName("uplink")] UniFiUplink? Uplink);

public sealed record UniFiUplink(
    [property: JsonPropertyName("txRateBps")] long? TxRateBps,
    [property: JsonPropertyName("rxRateBps")] long? RxRateBps);

// Clients
public sealed record UniFiClient(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("macAddress")] string? MacAddress,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("connectedAt")] string? ConnectedAt,
    [property: JsonPropertyName("uplinkDeviceId")] string? UplinkDeviceId);
