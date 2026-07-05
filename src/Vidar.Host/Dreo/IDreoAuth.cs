namespace Vidar.Host.Dreo;

public sealed record DreoManifestEntry(string Serial, string Model, string Name);
public sealed record DreoAuthResult(string TokenJson, string Region, IReadOnlyList<DreoManifestEntry> Devices);

public interface IDreoAuth
{
    Task<DreoAuthResult> PasswordLoginAsync(string email, string password, CancellationToken ct);
}
