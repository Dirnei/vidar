namespace Vidar.Communication.Zigbee2Mqtt;

public sealed record Zigbee2MqttConfig(
    string MqttHost,
    int MqttPort,
    string? MqttUser,
    string? MqttPassword,
    string BaseTopic);
