using System.Security.Cryptography;
using System.Text;

namespace Luma.Api.Billing;

public static class MercadoPagoWebhook
{
    public static bool Verify(string secret, string id, string requestId, string signature, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(requestId)) return false;
        var parts = signature.Split(',').Select(part => part.Trim().Split('=', 2)).Where(part => part.Length == 2).ToArray();
        var timestamps = parts.Where(part => part[0] == "ts").Select(part => part[1]).ToArray();
        if (timestamps.Length != 1 || !long.TryParse(timestamps[0], out var ts)) return false;
        var seconds = ts > 100_000_000_000 ? ts / 1000 : ts;
        if (Math.Abs(now.ToUnixTimeSeconds() - seconds) > 600) return false;
        var manifest = $"id:{id.ToLowerInvariant()};request-id:{requestId};ts:{timestamps[0]};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));
        foreach (var digest in parts.Where(part => part[0] == "v1"))
        {
            try { if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(digest[1]))) return true; }
            catch (FormatException) { }
        }
        return false;
    }
}
