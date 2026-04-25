using System.Text.RegularExpressions;

namespace Luma.Api.Services;

public static partial class DateParser
{
    private static readonly IReadOnlyDictionary<string, int> Months = new Dictionary<string, int>
    {
        ["janeiro"] = 1,
        ["jan"] = 1,
        ["fevereiro"] = 2,
        ["fev"] = 2,
        ["marco"] = 3,
        ["março"] = 3,
        ["mar"] = 3,
        ["abril"] = 4,
        ["abr"] = 4,
        ["maio"] = 5,
        ["mai"] = 5,
        ["junho"] = 6,
        ["jun"] = 6,
        ["julho"] = 7,
        ["jul"] = 7,
        ["agosto"] = 8,
        ["ago"] = 8,
        ["setembro"] = 9,
        ["set"] = 9,
        ["outubro"] = 10,
        ["out"] = 10,
        ["novembro"] = 11,
        ["nov"] = 11,
        ["dezembro"] = 12,
        ["dez"] = 12
    };

    public static bool IsUnknown(string normalized)
    {
        return normalized.Contains("nao lembro", StringComparison.Ordinal)
            || normalized.Contains("não lembro", StringComparison.Ordinal)
            || normalized.Contains("nao sei", StringComparison.Ordinal)
            || normalized.Contains("não sei", StringComparison.Ordinal)
            || normalized.Contains("esqueci", StringComparison.Ordinal);
    }

    public static DateOnly? ParseFlexibleDate(string raw, DateOnly today)
    {
        var normalized = MessageText.Normalize(raw);

        var relativeDaysAgo = ParseDaysAgo(raw);
        if (relativeDaysAgo is not null)
        {
            return today.AddDays(-relativeDaysAgo.Value);
        }

        var writtenMonthDate = ParseWrittenMonthDate(normalized, today);
        if (writtenMonthDate is not null)
        {
            return writtenMonthDate;
        }

        var numericDate = ParseNumericDate(normalized, today);
        if (numericDate is not null)
        {
            return numericDate;
        }

        var previousMonthDate = ParsePreviousMonthDate(normalized, today);
        if (previousMonthDate is not null)
        {
            return previousMonthDate;
        }

        return ParseDayOnlyDate(normalized, today);
    }

    public static int? ParseDaysAgo(string raw)
    {
        var normalized = MessageText.Normalize(raw);

        if (normalized.Contains("hoje", StringComparison.Ordinal))
        {
            return 0;
        }

        if (normalized.Contains("anteontem", StringComparison.Ordinal))
        {
            var anteontemIndex = normalized.LastIndexOf("anteontem", StringComparison.Ordinal);
            var prefix = anteontemIndex > 0 ? normalized[..anteontemIndex] : string.Empty;
            return 2 + CountBeforePrefixes(prefix);
        }

        if (normalized.Contains("ontem", StringComparison.Ordinal))
        {
            var ontemIndex = normalized.LastIndexOf("ontem", StringComparison.Ordinal);
            var prefix = ontemIndex > 0 ? normalized[..ontemIndex] : string.Empty;
            return 1 + CountBeforePrefixes(prefix);
        }

        var explicitMatch = ExplicitDaysAgo().Match(normalized);
        if (explicitMatch.Success && int.TryParse(explicitMatch.Groups["days"].Value, out var explicitDays))
        {
            return explicitDays is >= 0 and <= 120 ? explicitDays : null;
        }

        return null;
    }

    public static DateOnly? ParseDayOnlyDate(string raw, DateOnly today)
    {
        var normalized = MessageText.Normalize(raw);
        var match = DayOnlyPattern().Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups["day"].Value, out var day))
        {
            return null;
        }

        return BuildMostRecentDay(day, today.Month, today.Year, today);
    }

    private static DateOnly? ParseNumericDate(string normalized, DateOnly today)
    {
        var match = DatePattern().Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        var day = int.Parse(match.Groups["day"].Value);
        var month = int.Parse(match.Groups["month"].Value);
        var year = match.Groups["year"].Success
            ? int.Parse(match.Groups["year"].Value)
            : today.Year;

        if (year < 100)
        {
            year += 2000;
        }

        return BuildValidDate(day, month, year, today, allowYearFallback: !match.Groups["year"].Success);
    }

    private static DateOnly? ParseWrittenMonthDate(string normalized, DateOnly today)
    {
        var match = WrittenMonthPattern().Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups["day"].Value, out var day))
        {
            return null;
        }

        var monthText = match.Groups["month"].Value;
        if (!Months.TryGetValue(monthText, out var month))
        {
            return null;
        }

        var year = match.Groups["year"].Success
            ? int.Parse(match.Groups["year"].Value)
            : today.Year;

        if (year < 100)
        {
            year += 2000;
        }

        return BuildValidDate(day, month, year, today, allowYearFallback: !match.Groups["year"].Success);
    }

    private static DateOnly? ParsePreviousMonthDate(string normalized, DateOnly today)
    {
        var match = PreviousMonthPattern().Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups["day"].Value, out var day))
        {
            return null;
        }

        var previousMonth = today.AddMonths(-1);
        return BuildValidDate(day, previousMonth.Month, previousMonth.Year, today, allowYearFallback: false);
    }

    private static DateOnly? BuildMostRecentDay(int day, int month, int year, DateOnly today)
    {
        var candidate = BuildValidDate(day, month, year, today, allowYearFallback: false);
        if (candidate is null)
        {
            return null;
        }

        if (candidate.Value > today)
        {
            var previousMonth = new DateOnly(year, month, 1).AddMonths(-1);
            return BuildValidDate(day, previousMonth.Month, previousMonth.Year, today, allowYearFallback: false);
        }

        return candidate;
    }

    private static DateOnly? BuildValidDate(int day, int month, int year, DateOnly today, bool allowYearFallback)
    {
        try
        {
            var parsed = new DateOnly(year, month, day);
            if (allowYearFallback && parsed > today.AddDays(1))
            {
                parsed = parsed.AddYears(-1);
            }

            return parsed;
        }
        catch
        {
            return null;
        }
    }

    private static int CountBeforePrefixes(string value)
    {
        return BeforePrefix().Matches(value).Count;
    }

    [GeneratedRegex(@"(?<day>\d{1,2})[\/\-.](?<month>\d{1,2})(?:[\/\-.](?<year>\d{2,4}))?")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\bdia\s+(?<day>\d{1,2})\s+(?:de\s+)?(?<month>janeiro|jan|fevereiro|fev|marco|março|mar|abril|abr|maio|mai|junho|jun|julho|jul|agosto|ago|setembro|set|outubro|out|novembro|nov|dezembro|dez)(?:\s+(?:de\s+)?(?<year>\d{2,4}))?\b|\b(?<day>\d{1,2})\s+(?:de\s+)?(?<month>janeiro|jan|fevereiro|fev|marco|março|mar|abril|abr|maio|mai|junho|jun|julho|jul|agosto|ago|setembro|set|outubro|out|novembro|nov|dezembro|dez)(?:\s+(?:de\s+)?(?<year>\d{2,4}))?\b")]
    private static partial Regex WrittenMonthPattern();

    [GeneratedRegex(@"\bdia\s+(?<day>\d{1,2})\s+(?:do\s+|de\s+)?m[eê]s\s+passado\b|\b(?<day>\d{1,2})\s+(?:do\s+|de\s+)?m[eê]s\s+passado\b")]
    private static partial Regex PreviousMonthPattern();

    [GeneratedRegex(@"\bdia\s+(?<day>\d{1,2})\b")]
    private static partial Regex DayOnlyPattern();

    [GeneratedRegex(@"antes\s+de")]
    private static partial Regex BeforePrefix();

    [GeneratedRegex(@"(?:ha|há|faz|fazem|tem|come[cç]ou\s+ha|come[cç]ou\s+há)\s+(?:uns?\s+|umas?\s+|cerca\s+de\s+|mais\s+ou\s+menos\s+)?(?<days>\d{1,3})\s+dias?|(?<days>\d{1,3})\s+dias?\s+(?:atras|atrás)")]
    private static partial Regex ExplicitDaysAgo();
}
