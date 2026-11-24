using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TimeCheck.Application.Abstractions;

namespace TimeCheck.Infrastructure.Security;

public sealed class EncryptionOptions
{
    public const string Section = "Encryption";
    /// <summary>Key id used for new ciphertext. Old ids stay readable while their key is configured (rotation).</summary>
    public string ActiveKeyId { get; set; } = "";
    /// <summary>keyId → base64 32-byte AES-256 key.</summary>
    public Dictionary<string, string> Keys { get; init; } = [];
    /// <summary>Base64 32-byte HMAC key for blind indexes. Separate from the data keys.</summary>
    public string BlindIndexKey { get; set; } = "";
}

/// <summary>
/// AES-256-GCM, format "v{keyId}:{base64(nonce12 | ciphertext | tag16)}" (ADR 0003).
/// The key id travels with the ciphertext so keys can be rotated without a big-bang re-encrypt.
/// </summary>
public sealed class FieldEncryptor
{
    private const int NonceSize = 12, TagSize = 16;
    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    public FieldEncryptor(IOptions<EncryptionOptions> options)
    {
        var o = options.Value;
        _keys = o.Keys.ToDictionary(k => k.Key, k => DecodeKey(k.Value, $"Encryption:Keys:{k.Key}"));
        _activeKeyId = o.ActiveKeyId;
        if (!_keys.ContainsKey(_activeKeyId))
            throw new InvalidOperationException("Encryption:ActiveKeyId must name a configured key.");
    }

    public string Encrypt(string plaintext)
    {
        var key = _keys[_activeKeyId];
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + data.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize, data.Length), output.AsSpan(NonceSize + data.Length), AssociatedData(_activeKeyId));
        return $"v{_activeKeyId}:{Convert.ToBase64String(output)}";
    }

    public string Decrypt(string ciphertext)
    {
        var sep = ciphertext.IndexOf(':', StringComparison.Ordinal);
        if (sep < 2 || ciphertext[0] != 'v') throw new CryptographicException("Unrecognized ciphertext format.");
        var keyId = ciphertext[1..sep];
        if (!_keys.TryGetValue(keyId, out var key)) throw new CryptographicException($"Unknown encryption key id '{keyId}'.");

        var bytes = Convert.FromBase64String(ciphertext[(sep + 1)..]);
        if (bytes.Length < NonceSize + TagSize) throw new CryptographicException("Ciphertext too short.");
        var plain = new byte[bytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(bytes.AsSpan(0, NonceSize), bytes.AsSpan(NonceSize, plain.Length), bytes.AsSpan(NonceSize + plain.Length), plain, AssociatedData(keyId));
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] AssociatedData(string keyId) => Encoding.UTF8.GetBytes($"timecheck:v{keyId}");

    internal static byte[] DecodeKey(string base64, string name)
    {
        byte[] key;
        try { key = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new InvalidOperationException($"{name} is not valid base64."); }
        if (key.Length != 32) throw new InvalidOperationException($"{name} must be 32 bytes.");
        return key;
    }
}

public sealed class BlindIndexer(IOptions<EncryptionOptions> options) : IBlindIndexer
{
    private readonly byte[] _key = FieldEncryptor.DecodeKey(options.Value.BlindIndexKey, "Encryption:BlindIndexKey");

    public string Mobile(string mobile)
    {
        var digits = new string(mobile.Where(char.IsAsciiDigit).ToArray());
        // Treat local PH format (09xx) and international (+639xx) as the same number.
        if (digits.StartsWith("63", StringComparison.Ordinal) && digits.Length == 12) digits = "0" + digits[2..];
        return Hash("mobile", digits);
    }

    public string NameBirthdate(string firstName, string lastName, DateOnly birthdate) =>
        Hash("name_birthdate", $"{Normalize(firstName)}|{Normalize(lastName)}|{birthdate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

    public string Ip(string ip) => Hash("ip", ip);

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();

    private string Hash(string purpose, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}:{value}")));
}
