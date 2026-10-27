using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SupaBlox.Core;

/// <summary>
/// Ekspor/impor akun antar PC. DPAPI terikat ke akun Windows, jadi file ekspor dienkripsi ulang
/// dengan password: PBKDF2-SHA256 (200.000 iterasi) -> AES-256-GCM.
/// </summary>
public static class ExportCrypto
{
    private const int Iter = 200_000;

    public static string Encrypt(ExportPayload payload, string password)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, AppJson.Default.ExportPayload);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iter, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(plain);
        CryptographicOperations.ZeroMemory(key);
        var file = new ExportFile
        {
            Iterations = Iter,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(cipher),
        };
        return JsonSerializer.Serialize(file, AppJson.Default.ExportFile);
    }

    /// <exception cref="CryptographicException">Password salah atau file rusak.</exception>
    public static ExportPayload Decrypt(string json, string password)
    {
        var file = JsonSerializer.Deserialize(json, AppJson.Default.ExportFile)
                   ?? throw new InvalidDataException("File kosong.");
        if (file.Format != "supablox-export") throw new InvalidDataException("Bukan file ekspor SupaBlox.");
        if (file.Version != 1) throw new InvalidDataException($"Versi file {file.Version} belum didukung.");
        var salt = Convert.FromBase64String(file.Salt);
        var nonce = Convert.FromBase64String(file.Nonce);
        var tag = Convert.FromBase64String(file.Tag);
        var cipher = Convert.FromBase64String(file.Data);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Math.Max(file.Iterations, 10_000), HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return JsonSerializer.Deserialize(plain, AppJson.Default.ExportPayload) ?? new ExportPayload();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
