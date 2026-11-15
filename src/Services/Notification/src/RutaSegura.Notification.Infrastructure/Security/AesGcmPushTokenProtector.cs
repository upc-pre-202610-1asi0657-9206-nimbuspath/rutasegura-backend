using System.Security.Cryptography;
using System.Text;

namespace RutaSegura.Notification.Infrastructure.Security;

/// <summary>Clave externa de 256 bits. No genera ni guarda claves en el repositorio o en la base.</summary>
public sealed class AesGcmPushTokenProtector : IPushTokenProtector
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("RutaSegura.Notification.PushToken.v1");
    private readonly byte[] _key;

    public AesGcmPushTokenProtector(string base64Key)
    {
        try { _key = Convert.FromBase64String(base64Key); }
        catch (FormatException) { throw new ArgumentException("La clave de tokens push debe ser Base64 de 32 bytes.", nameof(base64Key)); }
        if (_key.Length != 32)
            throw new ArgumentException("La clave de tokens push debe tener 32 bytes.", nameof(base64Key));
    }

    public string Protect(string token)
    {
        var plaintext = Encoding.UTF8.GetBytes(token);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Purpose);
        CryptographicOperations.ZeroMemory(plaintext);
        return "v1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(ciphertext).ToArray());
    }

    public string Unprotect(string ciphertext)
    {
        if (!ciphertext.StartsWith("v1:", StringComparison.Ordinal))
            throw new CryptographicException("Formato de token protegido desconocido.");
        var bytes = Convert.FromBase64String(ciphertext[3..]);
        if (bytes.Length < 28) throw new CryptographicException("Token protegido incompleto.");
        var plaintext = new byte[bytes.Length - 28];
        using var aes = new AesGcm(_key, 16);
        try
        {
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext, Purpose);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
