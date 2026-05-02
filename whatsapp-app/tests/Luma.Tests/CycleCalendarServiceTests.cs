using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Luma.Tests;

public sealed class CycleCalendarServiceTests
{
    [Fact]
    public async Task BuildMonthAsync_ReturnsRecordedAndEstimatedCyclePins()
    {
        await using var db = CreateDbContext();
        var user = new LumaUser
        {
            PhoneNumber = "+5516992330309",
            OnboardingStep = OnboardingSteps.Completed,
            Preference = new UserPreference
            {
                LastPeriodStartDate = new DateOnly(2026, 4, 25),
                AverageCycleLength = 28,
                AveragePeriodLength = 4
            }
        };
        db.Users.Add(user);
        db.Cycles.Add(new Cycle
        {
            UserId = user.Id,
            StartDate = new DateOnly(2026, 4, 25),
            EndDate = new DateOnly(2026, 4, 28),
            Status = CycleStatus.Finished,
            CycleNumber = 1
        });
        db.CycleEvents.Add(new CycleEvent
        {
            UserId = user.Id,
            Type = CycleEventTypes.SexualActivity,
            Date = new DateOnly(2026, 5, 10),
            MetadataJson = "{}"
        });
        await db.SaveChangesAsync();

        var calendar = await new CycleCalendarService(db).BuildMonthAsync(user.Id, new YearMonth(2026, 5));

        Assert.NotNull(calendar);
        Assert.Contains(calendar.Days.Single(day => day.Date == new DateOnly(2026, 5, 10)).Items, item => item.Type == CalendarItemTypes.SexualActivityRecorded);
        Assert.Contains(calendar.Days.Single(day => day.Date == new DateOnly(2026, 5, 23)).Items, item => item.Type == CalendarItemTypes.PeriodStartPredicted);
        Assert.Contains(calendar.Days.Single(day => day.Date == new DateOnly(2026, 5, 9)).Items, item => item.Type == CalendarItemTypes.OvulationEstimated);
        Assert.Equal(new DateOnly(2026, 5, 23), calendar.Summary.NextPeriodDate);
    }

    [Fact]
    public async Task BuildMonthAsync_HidesPeriodPredictionsWhenPregnancyIsActive()
    {
        await using var db = CreateDbContext();
        var user = new LumaUser
        {
            PhoneNumber = "+5516992330310",
            OnboardingStep = OnboardingSteps.Completed,
            Preference = new UserPreference
            {
                LastPeriodStartDate = new DateOnly(2026, 3, 1),
                AverageCycleLength = 28,
                AveragePeriodLength = 4
            }
        };
        db.Users.Add(user);
        db.Pregnancies.Add(new Pregnancy
        {
            UserId = user.Id,
            LastPeriodDate = new DateOnly(2026, 3, 1),
            EstimatedDueDate = new DateOnly(2026, 12, 6)
        });
        await db.SaveChangesAsync();

        var calendar = await new CycleCalendarService(db).BuildMonthAsync(user.Id, new YearMonth(2026, 5));

        Assert.NotNull(calendar);
        Assert.True(calendar.Summary.ActivePregnancy);
        Assert.DoesNotContain(calendar.Days.SelectMany(day => day.Items), item => item.Type == CalendarItemTypes.PeriodStartPredicted);
        Assert.Contains(calendar.Days.SelectMany(day => day.Items), item => item.Type == CalendarItemTypes.PregnancyWeek);
    }

    private static LumaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LumaDbContext(options);
    }
}
