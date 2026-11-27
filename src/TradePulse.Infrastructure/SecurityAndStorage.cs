using System.Security.Cryptography;
using System.Text;
using TradePulse.Domain;

namespace TradePulse.Infrastructure;

public sealed class AesGcmDpapiDataProtector : ISensitiveDataProtector
{
    private readonly byte[] _key;
    private bool _disposed;

    public AesGcmDpapiDataProtector(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            var protectedBytes = File.ReadAllBytes(keyFilePath);
            _key = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath)!);
            _key = RandomNumberGenerator.GetBytes(32);
            var protectedBytes = ProtectedData.Protect(_key, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyFilePath, protectedBytes);
        }
    }

    public string Encrypt(string plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[12 + 16 + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, 12);
        Buffer.BlockCopy(tag, 0, combined, 12, 16);
        Buffer.BlockCopy(cipher, 0, combined, 28, cipher.Length);
        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var combined = Convert.FromBase64String(ciphertext);
        var nonce = combined.AsSpan(0, 12);
        var tag = combined.AsSpan(12, 16);
        var cipher = combined.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
