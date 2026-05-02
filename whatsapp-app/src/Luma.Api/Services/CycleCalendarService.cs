using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed record YearMonth(int Year, int Month)
{
    public DateOnly FirstDay => new(Year, Month, 1);
    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    public static bool TryParse(string? value, out YearMonth month)
    {
        month = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var parsedMonth)
            || parsedMonth is < 1 or > 12
            || year is < 1900 or > 2200)
        {
            return false;
        }

        month = new YearMonth(year, parsedMonth);
        return true;
    }
}

public sealed record CycleCalendar(YearMonth Month, IReadOnlyList<CalendarDay> Days, CalendarSummary Summary);

public sealed record CalendarDay(DateOnly Date, IReadOnlyList<CalendarItem> Items);

public sealed record CalendarItem(string Type, string Label, bool IsPrediction);

public sealed record CalendarSummary(
    DateOnly? LastPeriodDate,
    DateOnly? NextPeriodDate,
    bool ActivePregnancy,
    DateOnly? EstimatedDueDate);

public static class CalendarItemTypes
{
    public const string PeriodStartRecorded = "period_start_recorded";
    public const string PeriodEndRecorded = "period_end_recorded";
    public const string PeriodDayRecorded = "period_day_recorded";
    public const string PeriodStartPredicted = "period_start_predicted";
    public const string PeriodDayPredicted = "period_day_predicted";
    public const string FertileWindowEstimated = "fertile_window_estimated";
    public const string OvulationEstimated = "ovulation_estimated";
    public const string SexualActivityRecorded = "sexual_activity_recorded";
    public const string SymptomRecorded = "symptom_recorded";
    public const string MoodRecorded = "mood_recorded";
    public const string PregnancyWeek = "pregnancy_week";
    public const string EstimatedDueDate = "estimated_due_date";
}

public sealed class CycleCalendarService(LumaDbContext db)
{
    public async Task<CycleCalendar?> BuildMonthAsync(Guid userId, YearMonth month, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Include(item => item.Preference)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var firstDay = month.FirstDay;
        var lastDay = month.LastDay;
        var itemsByDate = Enumerable.Range(1, DateTime.DaysInMonth(month.Year, month.Month))
            .Select(day => new DateOnly(month.Year, month.Month, day))
            .ToDictionary(day => day, _ => new List<CalendarItem>());

        var cycles = await db.Cycles
            .AsNoTracking()
            .Where(cycle => cycle.UserId == userId
                && cycle.StartDate <= lastDay
                && (cycle.EndDate == null || cycle.EndDate >= firstDay))
            .OrderBy(cycle => cycle.StartDate)
            .ToListAsync(cancellationToken);

        foreach (var cycle in cycles)
        {
            AddCycleItems(itemsByDate, cycle, user.Preference?.AveragePeriodLength ?? 5);
        }

        var events = await db.CycleEvents
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Date >= firstDay && item.Date <= lastDay)
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);

        foreach (var cycleEvent in events)
        {
            AddEventItem(itemsByDate, cycleEvent);
        }

        var activePregnancy = await db.Pregnancies
            .AsNoTracking()
            .Where(pregnancy => pregnancy.UserId == userId && pregnancy.Status == PregnancyStatus.Active)
            .OrderByDescending(pregnancy => pregnancy.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (activePregnancy is not null)
        {
            AddPregnancyItems(itemsByDate, activePregnancy, firstDay, lastDay);
        }
        else
        {
            AddPredictedCycleItems(itemsByDate, user.Preference, firstDay, lastDay);
        }

        var lastPeriodDate = cycles.Select(cycle => (DateOnly?)cycle.StartDate)
            .Concat(user.Preference?.LastPeriodStartDate is null ? [] : [user.Preference.LastPeriodStartDate])
            .Where(date => date is not null)
            .Max();

        var nextPeriodDate = activePregnancy is null
            ? CalculateNextPeriodDate(user.Preference, firstDay, lastDay)
            : null;

        var days = itemsByDate
            .Select(pair => new CalendarDay(pair.Key, pair.Value
                .GroupBy(item => item.Type)
                .Select(group => group.First())
                .OrderBy(item => item.IsPrediction)
                .ThenBy(item => item.Type)
                .ToList()))
            .OrderBy(day => day.Date)
            .ToList();

        return new CycleCalendar(
            month,
            days,
            new CalendarSummary(
                lastPeriodDate,
                nextPeriodDate,
                activePregnancy is not null,
                activePregnancy?.EstimatedDueDate));
    }

    private static void AddCycleItems(Dictionary<DateOnly, List<CalendarItem>> itemsByDate, Cycle cycle, int averagePeriodLength)
    {
        var endDate = cycle.EndDate ?? cycle.StartDate.AddDays(Math.Max(1, averagePeriodLength) - 1);
        foreach (var date in EachDay(Max(cycle.StartDate, itemsByDate.Keys.Min()), Min(endDate, itemsByDate.Keys.Max())))
        {
            Add(itemsByDate, date, CalendarItemTypes.PeriodDayRecorded, "Menstruação", false);
        }

        Add(itemsByDate, cycle.StartDate, CalendarItemTypes.PeriodStartRecorded, "Início da menstruação", false);
        if (cycle.EndDate is not null)
        {
            Add(itemsByDate, cycle.EndDate.Value, CalendarItemTypes.PeriodEndRecorded, "Fim da menstruação", false);
        }
    }

    private static void AddEventItem(Dictionary<DateOnly, List<CalendarItem>> itemsByDate, CycleEvent cycleEvent)
    {
        switch (cycleEvent.Type)
        {
            case CycleEventTypes.SexualActivity:
                Add(itemsByDate, cycleEvent.Date, CalendarItemTypes.SexualActivityRecorded, "Relação sexual", false);
                break;
            case CycleEventTypes.Symptom:
            case CycleEventTypes.PregnancySymptom:
                Add(itemsByDate, cycleEvent.Date, CalendarItemTypes.SymptomRecorded, "Sintoma", false);
                break;
            case CycleEventTypes.Mood:
                Add(itemsByDate, cycleEvent.Date, CalendarItemTypes.MoodRecorded, "Humor", false);
                break;
        }
    }

    private static void AddPredictedCycleItems(Dictionary<DateOnly, List<CalendarItem>> itemsByDate, UserPreference? preference, DateOnly firstDay, DateOnly lastDay)
    {
        if (preference?.LastPeriodStartDate is null || preference.AverageCycleLength is < 21 or > 45)
        {
            return;
        }

        var periodLength = Math.Clamp(preference.AveragePeriodLength, 2, 10);
        var predicted = preference.LastPeriodStartDate.Value;
        while (predicted < firstDay.AddDays(-preference.AverageCycleLength))
        {
            predicted = predicted.AddDays(preference.AverageCycleLength);
        }

        while (predicted <= lastDay.AddDays(preference.AverageCycleLength))
        {
            if (predicted >= firstDay && predicted <= lastDay)
            {
                Add(itemsByDate, predicted, CalendarItemTypes.PeriodStartPredicted, "Previsão de início", true);
            }

            foreach (var date in EachDay(predicted, predicted.AddDays(periodLength - 1)))
            {
                if (date >= firstDay && date <= lastDay)
                {
                    Add(itemsByDate, date, CalendarItemTypes.PeriodDayPredicted, "Previsão menstrual", true);
                }
            }

            var ovulation = predicted.AddDays(-14);
            foreach (var date in EachDay(ovulation.AddDays(-5), ovulation))
            {
                if (date >= firstDay && date <= lastDay)
                {
                    Add(itemsByDate, date, CalendarItemTypes.FertileWindowEstimated, "Janela fértil estimada", true);
                }
            }

            if (ovulation >= firstDay && ovulation <= lastDay)
            {
                Add(itemsByDate, ovulation, CalendarItemTypes.OvulationEstimated, "Ovulação estimada", true);
            }

            predicted = predicted.AddDays(preference.AverageCycleLength);
        }
    }

    private static void AddPregnancyItems(Dictionary<DateOnly, List<CalendarItem>> itemsByDate, Pregnancy pregnancy, DateOnly firstDay, DateOnly lastDay)
    {
        var reference = pregnancy.LastPeriodDate ?? DateOnly.FromDateTime(pregnancy.CreatedAt.ToOffset(TimeSpan.FromHours(-3)).Date);
        foreach (var date in EachDay(firstDay, lastDay))
        {
            var days = date.DayNumber - reference.DayNumber;
            if (days >= 0 && days % 7 == 0)
            {
                Add(itemsByDate, date, CalendarItemTypes.PregnancyWeek, $"Semana {Math.Max(1, days / 7 + 1)}", true);
            }
        }

        if (pregnancy.EstimatedDueDate is { } dueDate && dueDate >= firstDay && dueDate <= lastDay)
        {
            Add(itemsByDate, dueDate, CalendarItemTypes.EstimatedDueDate, "Semana prevista para o parto", true);
        }
    }

    private static DateOnly? CalculateNextPeriodDate(UserPreference? preference, DateOnly firstDay, DateOnly lastDay)
    {
        if (preference?.LastPeriodStartDate is null || preference.AverageCycleLength is < 21 or > 45)
        {
            return null;
        }

        var predicted = preference.LastPeriodStartDate.Value;
        while (predicted < firstDay)
        {
            predicted = predicted.AddDays(preference.AverageCycleLength);
        }

        return predicted <= lastDay.AddDays(preference.AverageCycleLength) ? predicted : null;
    }

    private static void Add(Dictionary<DateOnly, List<CalendarItem>> itemsByDate, DateOnly date, string type, string label, bool isPrediction)
    {
        if (itemsByDate.TryGetValue(date, out var items))
        {
            items.Add(new CalendarItem(type, label, isPrediction));
        }
    }

    private static IEnumerable<DateOnly> EachDay(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static DateOnly Min(DateOnly left, DateOnly right) => left <= right ? left : right;
    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;
}
