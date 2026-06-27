namespace Vidar.Communication.Dyson;

public enum DysonTransport { Local, NeedsConnection, Offline }

public static class DysonDeviceState
{
    public static DysonTransport Evaluate(string? ip, bool connected)
    {
        if (string.IsNullOrWhiteSpace(ip)) return DysonTransport.NeedsConnection;
        return connected ? DysonTransport.Local : DysonTransport.Offline;
    }
}
