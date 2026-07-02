namespace Vidar.Communication.Ecowitt;

public sealed record EcowittConfig(
    string MqttHost,
    int MqttPort,
    string? MqttUser,
    string? MqttPassword,
    string Topic,
    int StaleAfterSeconds);
