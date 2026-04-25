using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Luma.Api.Services;

public static partial class MessageText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return MultipleSpaces().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ");
    }

    public static int? ExtractFirstInteger(string value)
    {
        var match = Integer().Match(value);
        return match.Success && int.TryParse(match.Value, out var parsed) ? parsed : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Integer();
}
