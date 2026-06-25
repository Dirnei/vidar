using System.Security.Cryptography;
using System.Text;
using Vidar.Host.Dyson;
using Xunit;

namespace Vidar.Host.Tests.Dyson;

public class DysonCloudClientDecryptTests
{
    private static string Encrypt(string plaintext)
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return Convert.ToBase64String(enc.TransformFinalBlock(bytes, 0, bytes.Length));
    }

    [Fact]
    public void DecryptLocalCredentials_ReturnsApPasswordHash()
    {
        var encrypted = Encrypt("{\"serial\":\"X6p-EU-SKA0802A\",\"apPasswordHash\":\"hashed-mqtt-pw\"}");

        var result = DysonCloudClient.DecryptLocalCredentials(encrypted);

        Assert.Equal("hashed-mqtt-pw", result);
    }
}
