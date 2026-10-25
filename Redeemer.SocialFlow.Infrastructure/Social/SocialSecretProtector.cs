using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
namespace Redeemer.SocialFlow.Infrastructure.Social;

// Portable authenticated encryption. Keys come from a secret store/environment, never the business database.
public sealed class SocialSecretProtector(IConfiguration config) : ISocialSecretProtector
{
    private string Active => config["SocialConnections:Encryption:ActiveKeyId"] ?? "";
    private byte[] Key(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Contains('.')) throw new CryptographicException();
        var key = Convert.FromBase64String(config[$"SocialConnections:Encryption:Keys:{id}"] ?? "");
        if (key.Length != 32) throw new CryptographicException();
        return key;
    }
    public bool Available { get { try { var key = Key(Active); CryptographicOperations.ZeroMemory(key); return true; } catch { return false; } } }
    public string Protect(string value, string purpose)
    {
        var key = Key(Active);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var plain = Encoding.UTF8.GetBytes(value);
            var encrypted = new byte[plain.Length]; var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, encrypted, tag, Encoding.UTF8.GetBytes(purpose)); CryptographicOperations.ZeroMemory(plain);
            return string.Join('.', Active, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(encrypted));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public string Unprotect(string value, string purpose)
    {
        var parts = value.Split('.'); if (parts.Length != 4) throw new CryptographicException();
        var key = Key(parts[0]);
        try
        {
            var bytes = Convert.FromBase64String(parts[3]); var plain = new byte[bytes.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(parts[1]), bytes, Convert.FromBase64String(parts[2]), plain, Encoding.UTF8.GetBytes(purpose));
            var result = Encoding.UTF8.GetString(plain); CryptographicOperations.ZeroMemory(plain); return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
