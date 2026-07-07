namespace Vidar.Host.Loxone;

public sealed record LoxoneProbeResult(string Serial, int ControlCount, int RoomCount);

public interface ILoxoneSidecar
{
    Task<LoxoneProbeResult> ProbeAsync(string host, string user, string password, CancellationToken ct);
}
