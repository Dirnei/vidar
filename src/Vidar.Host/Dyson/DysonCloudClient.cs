using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vidar.Host.Dyson;

public sealed partial class DysonCloudClient
{
    public static string DecryptLocalCredentials(string encryptedBase64)
    {
        var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var iv = new byte[16];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var cipher = Convert.FromBase64String(encryptedBase64);
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        var json = Encoding.UTF8.GetString(plain);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("apPasswordHash").GetString()
            ?? throw new InvalidOperationException("apPasswordHash missing in decrypted credentials");
    }
}
