using System.Text.RegularExpressions;

namespace Luma.Api.Services;

public static partial class PhoneNumber
{
    public static string Normalize(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["whatsapp:".Length..];
        }

        var digits = DigitsOnly().Replace(trimmed, string.Empty);
        return string.IsNullOrWhiteSpace(digits) ? trimmed : $"+{digits}";
    }

    public static string Mask(string value)
    {
        var normalized = Normalize(value);
        return normalized.Length <= 6
            ? normalized
            : $"{normalized[..4]}***{normalized[^4..]}";
    }

    [GeneratedRegex("[^0-9]")]
    private static partial Regex DigitsOnly();
}
