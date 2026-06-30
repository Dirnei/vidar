namespace Vidar.Communication.Roborock;

public sealed record RoborockDeviceCredential(
    string Duid, string Model, string Name, string LocalKey, string Ip);
