namespace Vidar.Communication.Dyson;

public sealed record DysonDeviceCredential(string Serial, string ProductType, string MqttPassword, string? Ip);
