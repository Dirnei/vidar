using System.Reflection;
using Xunit;

namespace Vidar.Host.Tests.OAuth;

public class OAuthControllerSpotifyTests
{
    [Fact]
    public void KnownIntegrations_IncludesSpotify()
    {
        var field = typeof(Vidar.Host.Api.OAuthController)
            .GetField("KnownIntegrations", BindingFlags.NonPublic | BindingFlags.Static);
        var set = (HashSet<string>)field!.GetValue(null)!;
        Assert.Contains("spotify", set);
    }
}
