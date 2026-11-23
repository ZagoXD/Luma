using Luma.Api.Data;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class UsageLedgerTests
{
    [Fact]
    public async Task Onboarding_and_normal_AI_share_the_monthly_budget()
    {
        var service = new UsageLedger(db, Options.Create(new UsageOptions { BasicMonthlyMessages = 1 }), TimeProvider.System);
        var account = Guid.NewGuid();
        Assert.True((await service.ReserveAsync(account, "basico", "signup", "onboarding", 1)).Allowed);
        Assert.False((await service.ReserveAsync(account, "basico", "normal", "ai", 1)).Allowed);
        Assert.Equal(1, (await service.SnapshotAsync(account, "basico", default)).MonthlyUsed);
    }
    [Fact]
    public async Task Monthly_AI_is_atomic_with_daily_and_resets_only_at_local_month_boundary()
    {
        var clock = new Clock(new(2026, 11, 29, 15, 0, 0, TimeSpan.Zero));
        var service = new UsageLedger(db, Options.Create(new UsageOptions { BasicMonthlyMessages = 2 }), clock);
        var id = Guid.NewGuid();
        Assert.True((await service.ReserveAsync(id, "basico", "first", "ai", 1)).Allowed);
        clock.Now = clock.Now.AddDays(1);
        Assert.True((await service.ReserveAsync(id, "basico", "second", "ai", 1)).Allowed);
        var rejected = await service.ReserveAsync(id, "basico", "third", "ai", 1);
        Assert.False(rejected.Allowed);
        Assert.Contains("mês", rejected.Message);
        var state = await service.SnapshotAsync(id, "basico", default);
        Assert.Equal(1, state.DailyUsed);
        Assert.Equal(2, state.MonthlyUsed);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 3, 0, 0, TimeSpan.Zero), state.MonthlyResetsAt);
        clock.Now = state.MonthlyResetsAt;
        Assert.True((await service.ReserveAsync(id, "basico", "new-month", "ai", 1)).Allowed);
    }

    [Fact]
    public async Task Monthly_upgrade_uses_existing_consumption_and_audio_cannot_bypass_monthly_AI()
    {
        var service = new UsageLedger(db, Options.Create(new UsageOptions { BasicMonthlyMessages = 1, EssentialMonthlyMessages = 2 }), TimeProvider.System);
        var id = Guid.NewGuid();
        Assert.True((await service.ReserveAsync(id, "basico", "one", "ai", 1)).Allowed);
        Assert.True((await service.ReserveAudioAsync(id, "essencial", "two", 30)).Allowed);
        Assert.False((await service.ReserveAudioAsync(id, "essencial", "three", 30)).Allowed);
        var state = await service.SnapshotAsync(id, "essencial", default);
        Assert.Equal(2, state.MonthlyUsed);
        Assert.Equal(30, state.AudioSecondsUsed);
        Assert.True((await service.ReserveAsync(id, "basico", "one", "ai", 1)).Duplicate);
    }

    [Fact]
    public async Task Existing_daily_receipts_are_counted_when_monthly_buckets_are_introduced()
    {
        var clock = new Clock(new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero));
        var id = Guid.NewGuid();
        await using var context = new LumaDbContext(db);
        context.UsageReceipts.Add(new() { Id = "legacy", AccountId = id, Kind = "ai", Units = 1, CreatedAt = clock.Now });
        await context.SaveChangesAsync();
        var service = new UsageLedger(db, Options.Create(new UsageOptions { BasicMonthlyMessages = 1 }), clock);
        Assert.Equal(1, (await service.SnapshotAsync(id, "basico", default)).MonthlyUsed);
        Assert.False((await service.ReserveAsync(id, "basico", "extra", "ai", 1)).Allowed);
    }
    private readonly DbContextOptions<LumaDbContext> db = new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    [Fact]
    public async Task Daily_admission_is_idempotent_and_resets_by_server_calendar_not_plan_change()
    {
        var time = new Clock(new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero));
        var service = new UsageLedger(db, Options.Create(new UsageOptions()), time);
        var account = Guid.NewGuid();
        for (var i = 0; i < 10; i++) Assert.True((await service.ReserveAsync(account, "basico", "m" + i, "ai", 1)).Allowed);
        Assert.True((await service.ReserveAsync(account, "basico", "m0", "ai", 1)).Duplicate);
        Assert.False((await service.ReserveAsync(account, "basico", "extra", "ai", 1)).Allowed);
        Assert.True((await service.ReserveAsync(account, "essencial", "upgrade", "ai", 1)).Allowed);
        time.Now = time.Now.AddDays(1);
        Assert.True((await service.ReserveAsync(account, "basico", "tomorrow", "ai", 1)).Allowed);
    }
    [Fact]
    public async Task Audio_has_monthly_seconds_and_an_individual_maximum_without_spending_AI_again()
    {
        var service = new UsageLedger(db, Options.Create(new UsageOptions()), new Clock(new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero)));
        var id = Guid.NewGuid();
        Assert.False((await service.ReserveAsync(id, "basico", "x", "audio", 30)).Allowed);
        Assert.False((await service.ReserveAsync(id, "essencial", "long", "audio", 61)).Allowed);
        for (var i = 0; i < 10; i++) Assert.True((await service.ReserveAsync(id, "essencial", "a" + i, "audio", 60)).Allowed);
        Assert.False((await service.ReserveAsync(id, "essencial", "over", "audio", 1)).Allowed);
        Assert.True((await service.ReserveAsync(id, "essencial", "text", "ai", 1)).Allowed);
    }
    [Fact]
    public async Task Concurrent_admissions_cannot_exceed_the_limit()
    {
        var service = new UsageLedger(db, Options.Create(new UsageOptions()), TimeProvider.System); var id = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => service.ReserveAsync(id, "basico", "m" + i, "ai", 1)));
        Assert.Equal(10, results.Count(x => x.Allowed));
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }

    [Fact]
    public async Task Rejected_monthly_audio_does_not_spend_the_daily_AI_allowance()
    {
        var service = new UsageLedger(db, Options.Create(new UsageOptions { MonthlyAudioSeconds = 60 }), TimeProvider.System);
        var account = Guid.NewGuid();
        Assert.True((await service.ReserveAudioAsync(account, "essencial", "first", 60)).Allowed);
        Assert.False((await service.ReserveAudioAsync(account, "essencial", "second", 1)).Allowed);
        await using var context = new LumaDbContext(db);
        Assert.Equal(1L, (await context.UsageBuckets.SingleAsync(x => x.Kind == "ai")).Units);
        Assert.Equal(3, await context.UsageReceipts.CountAsync());
    }

    [Fact]
    public async Task Daily_reset_is_at_Sao_Paulo_midnight_not_UTC_midnight()
    {
        var clock = new Clock(new(2026, 11, 23, 2, 59, 0, TimeSpan.Zero));
        var service = new UsageLedger(db, Options.Create(new UsageOptions { BasicDailyMessages = 1 }), clock);
        var account = Guid.NewGuid();
        Assert.True((await service.ReserveAsync(account, "basico", "before", "ai", 1)).Allowed);
        Assert.False((await service.ReserveAsync(account, "basico", "blocked", "ai", 1)).Allowed);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True((await service.ReserveAsync(account, "basico", "after", "ai", 1)).Allowed);
    }
}
