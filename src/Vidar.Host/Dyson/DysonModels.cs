namespace Vidar.Host.Dyson;

public sealed record DysonDevice(string Serial, string ProductType, string Name, string MqttPassword, string? Variant);
