namespace Vidar.Communication.Bambu;

public sealed record BambuConfig(
    string Host,
    string Serial,
    string AccessCode,
    string Model,
    string Name);
