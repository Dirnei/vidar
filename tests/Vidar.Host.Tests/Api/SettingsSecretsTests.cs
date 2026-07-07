using Vidar.Host.Api;

namespace Vidar.Host.Tests.Api;

public class SettingsSecretsTests
{
    [Theory]
    [InlineData("mqttPassword")]
    [InlineData("apiKey")]
    [InlineData("password")]
    [InlineData("account.token")]
    public void Redact_SecretKey_ReplacesValueWithSentinel(string key)
    {
        var settings = new Dictionary<string, string> { [key] = "supersecret" };

        var result = SettingsSecrets.Redact(settings);

        Assert.Equal(SettingsSecrets.RedactedSentinel, result[key]);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("siteId")]
    [InlineData("baseTopic")]
    public void Redact_NonSecretKey_LeavesValueUnchanged(string key)
    {
        var settings = new Dictionary<string, string> { [key] = "some-value" };

        var result = SettingsSecrets.Redact(settings);

        Assert.Equal("some-value", result[key]);
    }

    [Fact]
    public void Redact_MiniserversJsonArray_RedactsNestedPasswordOnly()
    {
        const string json = """[{"serial":"A","host":"h","user":"u","password":"p"}]""";
        var settings = new Dictionary<string, string> { ["miniservers"] = json };

        var result = SettingsSecrets.Redact(settings);

        using var doc = System.Text.Json.JsonDocument.Parse(result["miniservers"]);
        var entry = doc.RootElement[0];
        Assert.Equal("A", entry.GetProperty("serial").GetString());
        Assert.Equal("h", entry.GetProperty("host").GetString());
        Assert.Equal("u", entry.GetProperty("user").GetString());
        Assert.Equal(SettingsSecrets.RedactedSentinel, entry.GetProperty("password").GetString());
    }

    [Fact]
    public void Redact_NonJsonValueUnderNonSecretKey_LeftUnchanged()
    {
        var settings = new Dictionary<string, string> { ["baseTopic"] = "not { valid json" };

        var result = SettingsSecrets.Redact(settings);

        Assert.Equal("not { valid json", result["baseTopic"]);
    }

    [Fact]
    public void Redact_DoesNotMutateInputDictionary()
    {
        var settings = new Dictionary<string, string> { ["password"] = "supersecret" };

        SettingsSecrets.Redact(settings);

        Assert.Equal("supersecret", settings["password"]);
    }

    [Fact]
    public void PreserveRedacted_SentinelWithExistingValue_RestoresRealSecret()
    {
        var incoming = new Dictionary<string, string>
        {
            ["apiKey"] = SettingsSecrets.RedactedSentinel,
            ["host"] = "new",
        };
        var existing = new Dictionary<string, string>
        {
            ["apiKey"] = "realkey",
            ["host"] = "old",
        };

        var result = SettingsSecrets.PreserveRedacted(incoming, existing);

        Assert.Equal("realkey", result["apiKey"]);
        Assert.Equal("new", result["host"]);
    }

    [Fact]
    public void PreserveRedacted_SentinelWithNoExistingKey_IsDropped()
    {
        var incoming = new Dictionary<string, string> { ["apiKey"] = SettingsSecrets.RedactedSentinel };
        IReadOnlyDictionary<string, string>? existing = null;

        var result = SettingsSecrets.PreserveRedacted(incoming, existing);

        Assert.False(result.ContainsKey("apiKey"));
    }

    [Fact]
    public void PreserveRedacted_NonSentinelValues_PassThroughUnchanged()
    {
        var incoming = new Dictionary<string, string> { ["host"] = "brandnew" };
        var existing = new Dictionary<string, string> { ["host"] = "old" };

        var result = SettingsSecrets.PreserveRedacted(incoming, existing);

        Assert.Equal("brandnew", result["host"]);
    }
}
