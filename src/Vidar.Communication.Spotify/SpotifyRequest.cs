using System.Net.Http;

namespace Vidar.Communication.Spotify;

// Describes one outbound Spotify Web API call, independent of the HttpClient that sends it.
// Keeping this a pure value lets the command builder be unit-tested without any network.
public sealed record SpotifyRequest(
    HttpMethod Method,
    string Path,
    IReadOnlyDictionary<string, string?> Query,
    string? JsonBody);
