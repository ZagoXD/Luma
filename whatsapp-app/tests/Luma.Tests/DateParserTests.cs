using Luma.Api.Services;

namespace Luma.Tests;

public sealed class DateParserTests
{
    private static readonly DateOnly Today = new(2026, 4, 25);

    [Theory]
    [InlineData("começou hoje", "2026-04-25")]
    [InlineData("começou ontem", "2026-04-24")]
    [InlineData("começou antes de ontem", "2026-04-23")]
    [InlineData("começou antes de antes de ontem", "2026-04-22")]
    [InlineData("começou há uns 5 dias", "2026-04-20")]
    [InlineData("fazem 3 dias", "2026-04-22")]
    public void ParseFlexibleDate_resolves_relative_dates(string input, string expected)
    {
        var parsed = DateParser.ParseFlexibleDate(input, Today);

        Assert.Equal(DateOnly.Parse(expected), parsed);
    }

    [Theory]
    [InlineData("começou dia 10", "2026-04-10")]
    [InlineData("começou dia 10 de abril", "2026-04-10")]
    [InlineData("dia 30 do mes passado", "2026-03-30")]
    [InlineData("começou 30 mes passado", "2026-03-30")]
    [InlineData("começou 10/04", "2026-04-10")]
    public void ParseFlexibleDate_resolves_ambiguous_calendar_dates(string input, string expected)
    {
        var parsed = DateParser.ParseFlexibleDate(input, Today);

        Assert.Equal(DateOnly.Parse(expected), parsed);
    }

    [Fact]
    public void ParseFlexibleDate_uses_previous_month_when_day_only_would_be_future()
    {
        var parsed = DateParser.ParseFlexibleDate("começou dia 10", new DateOnly(2026, 4, 8));

        Assert.Equal(new DateOnly(2026, 3, 10), parsed);
    }
}
