using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Luma.Api.Services;

public sealed class PrivacyOptions
{
    public bool EncryptionEnabled { get; set; }
    public string EncryptionKey { get; set; } = string.Empty;
    public string LookupPepper { get; set; } = string.Empty;
    public string ActiveKeyId { get; set; } = "local-dev";
}

public static class PrivacyRuntime
{
    private static readonly object Gate = new();
    private static PrivacyOptions _options = new();
    private static byte[]? _encryptionKey;
    private static byte[]? _lookupPepper;

    public static void Configure(PrivacyOptions options)
    {
        lock (Gate)
        {
            _options = options;
            _encryptionKey = TryDecodeKey(options.EncryptionKey, 32);
            _lookupPepper = TryDecodeKey(options.LookupPepper, 32) ?? Encoding.UTF8.GetBytes("luma-local-lookup-pepper-change-before-production");
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _options = new PrivacyOptions();
            _encryptionKey = null;
            _lookupPepper = Encoding.UTF8.GetBytes("luma-local-lookup-pepper-change-before-production");
        }
    }

    public static bool EncryptionEnabled => _options.EncryptionEnabled && _encryptionKey is not null;

    public static string Protect(string? value, string purpose)
    {
        if (string.IsNullOrEmpty(value) || !EncryptionEnabled || IsEnvelope(value))
        {
            return value ?? string.Empty;
        }

        var key = _encryptionKey!;
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var additionalData = Encoding.UTF8.GetBytes(purpose);

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, additionalData);

        return JsonSerializer.Serialize(new EncryptedFieldEnvelope(
            v: 1,
            alg: "A256GCM",
            kid: string.IsNullOrWhiteSpace(_options.ActiveKeyId) ? "local-dev" : _options.ActiveKeyId,
            nonce: Convert.ToBase64String(nonce),
            ciphertext: Convert.ToBase64String(ciphertext),
            tag: Convert.ToBase64String(tag)));
    }

    public static string Unprotect(string? value, string purpose)
    {
        if (string.IsNullOrEmpty(value) || !IsEnvelope(value))
        {
            return value ?? string.Empty;
        }

        if (!EncryptionEnabled)
        {
            return value;
        }

        var envelope = JsonSerializer.Deserialize<EncryptedFieldEnvelope>(value)
            ?? throw new InvalidOperationException("Encrypted field envelope is invalid.");

        if (envelope.alg != "A256GCM")
        {
            throw new InvalidOperationException("Encrypted field algorithm is not supported.");
        }

        var nonce = Convert.FromBase64String(envelope.nonce);
        var ciphertext = Convert.FromBase64String(envelope.ciphertext);
        var tag = Convert.FromBase64String(envelope.tag);
        var plaintext = new byte[ciphertext.Length];
        var additionalData = Encoding.UTF8.GetBytes(purpose);

        try
        {
            using var aes = new AesGcm(_encryptionKey!, AesGcm.TagByteSizes.MaxSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, additionalData);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Encrypted field could not be decrypted with the configured key and purpose.", ex);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    public static string LookupHash(string? value, string purpose)
    {
        var normalized = NormalizeLookupValue(value, purpose);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        using var hmac = new HMACSHA256(_lookupPepper ?? Encoding.UTF8.GetBytes("luma-local-lookup-pepper-change-before-production"));
        var input = Encoding.UTF8.GetBytes($"{purpose}:{normalized}");
        return Convert.ToHexString(hmac.ComputeHash(input)).ToLowerInvariant();
    }

    public static bool IsEnvelope(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.TryGetProperty("alg", out var alg)
                && alg.GetString() == "A256GCM"
                && document.RootElement.TryGetProperty("ciphertext", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeLookupValue(string? value, string purpose)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (purpose.Contains("phone", StringComparison.OrdinalIgnoreCase))
        {
            return PhoneNumber.Normalize(value);
        }

        if (purpose.Contains("cpf", StringComparison.OrdinalIgnoreCase))
        {
            return AccountInputNormalizer.OnlyDigits(value);
        }

        if (purpose.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return value.Trim().ToLowerInvariant();
        }

        return value.Trim();
    }

    private static byte[]? TryDecodeKey(string value, int expectedBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            return decoded.Length == expectedBytes ? decoded : null;
        }
        catch (FormatException)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return bytes.Length >= expectedBytes ? bytes[..expectedBytes] : null;
        }
    }

    private sealed record EncryptedFieldEnvelope(int v, string alg, string kid, string nonce, string ciphertext, string tag);
}
