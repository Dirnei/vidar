namespace Vidar.Host.Roborock;

public sealed record RoborockManifestEntry(string Duid, string Name, string Model, string LocalKey, string Ip);
public sealed record RoborockAuthResult(string UserDataJson, IReadOnlyList<RoborockManifestEntry> Devices);

public interface IRoborockAuth
{
    Task<RoborockAuthResult> PasswordLoginAsync(string email, string password, CancellationToken ct);
    Task RequestCodeAsync(string email, CancellationToken ct);
    Task<RoborockAuthResult> CodeLoginAsync(string email, string code, CancellationToken ct);
}
