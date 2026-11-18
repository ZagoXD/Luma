using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class NotificationProcessorTests
{
    [Theory]
    [InlineData(180, true)]
    [InlineData(30, false)]
    public async Task Missing_callbacks_are_reconciled_by_reading_the_provider_without_resending(int ageSeconds, bool reconciles)
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        var user = await AddDailyCheckinAsync(db, now);
        db.NotificationPreferences.Single().SymptomCheckinEnabled = false;
        var sid = "MM" + new string('1', 32);
        var delivery = new NotificationDelivery { UserId = user.Id, Type = NotificationTypes.SymptomCheckin,
            Status = NotificationDeliveryStatuses.Submitted, ProviderMessageId = sid,
            ScheduledFor = now.AddSeconds(-ageSeconds), UpdatedAt = now.AddSeconds(-ageSeconds) };
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        var sender = new CapturingNotificationSender { ProviderStatus = new NotificationProviderStatus(sid, "read", null) };
        await CreateProcessor(db, sender, now).RunDueNotificationsAsync();
        Assert.Empty(sender.Messages);
        Assert.Equal(reconciles ? NotificationDeliveryStatuses.Read : NotificationDeliveryStatuses.Submitted, delivery.Status);
        Assert.Equal(reconciles ? 1 : 0, sender.StatusLookups);
    }

    [Theory]
    [InlineData("2026-05-11T02:30:00Z", "America/Sao_Paulo", 23, 30, "2026-05-10")]
    [InlineData("2026-05-10T10:30:00Z", "Pacific/Kiritimati", 0, 30, "2026-05-11")]
    public async Task Reminders_and_deduplication_use_the_users_local_date(string utc, string zone, int hour, int minute, string date)
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.Parse(utc);
        var day = DateOnly.Parse(date);
        var user = await CreateEssentialUserAsync(db, "+5511999990001", "Teste", now);
        db.UserPreferences.Add(new UserPreference { UserId = user.Id, LastPeriodStartDate = day.AddDays(1 - 29), AverageCycleLength = 29 });
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, User = user,
            PeriodReminderEnabled = true, PeriodReminderTime = new TimeOnly(hour, minute), TimeZone = zone });
        await db.SaveChangesAsync();
        var sender = new CapturingNotificationSender();
        await CreateProcessor(db, sender, now).RunDueNotificationsAsync();
        Assert.Equal(NotificationTypes.PeriodExpectedTomorrow, Assert.Single(sender.Messages).TemplateKey);
        Assert.Equal(day, Assert.Single(db.NotificationDeliveries).ScheduledForDate);
    }

    [Fact]
    public async Task Transient_failure_retries_with_backoff_without_creating_duplicate_delivery()
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        await AddDailyCheckinAsync(db, now);
        var sender = new CapturingNotificationSender();
        sender.Results.Enqueue(new NotificationSendResult(false, null, "rate_limited", Retryable: true));
        await CreateProcessor(db, sender, now).RunDueNotificationsAsync();
        await CreateProcessor(db, sender, now.AddSeconds(30)).RunDueNotificationsAsync();
        Assert.Single(sender.Messages);
        await CreateProcessor(db, sender, now.AddMinutes(1)).RunDueNotificationsAsync();
        await CreateProcessor(db, sender, now.AddMinutes(2)).RunDueNotificationsAsync();
        Assert.Equal(2, sender.Messages.Count);
        var delivery = Assert.Single(db.NotificationDeliveries);
        Assert.Equal(2, delivery.Attempts);
        Assert.Equal(NotificationDeliveryStatuses.Submitted, delivery.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Permanent_or_ambiguous_failures_are_not_blindly_retried(bool unknown)
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        await AddDailyCheckinAsync(db, now);
        var sender = new CapturingNotificationSender();
        sender.Results.Enqueue(new NotificationSendResult(false, null, "test_error", DeliveryUnknown: unknown));
        await CreateProcessor(db, sender, now).RunDueNotificationsAsync();
        await CreateProcessor(db, sender, now.AddMinutes(1)).RunDueNotificationsAsync();
        Assert.Single(sender.Messages);
        Assert.Equal(unknown ? NotificationDeliveryStatuses.Unknown : NotificationDeliveryStatuses.Failed, db.NotificationDeliveries.Single().Status);
    }

    [Fact]
    public async Task Retries_stop_after_three_attempts()
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        await AddDailyCheckinAsync(db, now);
        var sender = new CapturingNotificationSender();
        for (var i = 0; i < 5; i++) sender.Results.Enqueue(new NotificationSendResult(false, null, "rate_limited", Retryable: true));
        foreach (var minute in new[] { 0, 1, 3, 4 }) await CreateProcessor(db, sender, now.AddMinutes(minute)).RunDueNotificationsAsync();
        Assert.Equal(3, sender.Messages.Count);
        Assert.Equal(NotificationDeliveryStatuses.Failed, Assert.Single(db.NotificationDeliveries).Status);
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("expired")]
    [InlineData("no_consent")]
    [InlineData("pregnant")]
    [InlineData("disabled")]
    public async Task Ineligible_users_do_not_receive_notifications(string reason)
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        var user = await AddDailyCheckinAsync(db, now);
        if (reason == "basic") db.AccountSubscriptions.Single().PlanCode = "basico";
        if (reason == "expired") db.AccountSubscriptions.Single().CurrentPeriodEndsAt = now.AddSeconds(-1);
        if (reason == "no_consent") user.ConsentAcceptedAt = null;
        if (reason == "disabled") db.NotificationPreferences.Single().SymptomCheckinEnabled = false;
        if (reason == "pregnant") db.Pregnancies.Add(new Pregnancy { UserId = user.Id, Status = PregnancyStatus.Active });
        await db.SaveChangesAsync();
        var sender = new CapturingNotificationSender();
        await CreateProcessor(db, sender, now).RunDueNotificationsAsync();
        Assert.Empty(sender.Messages);
    }

    private static async Task<LumaUser> AddDailyCheckinAsync(LumaDbContext db, DateTimeOffset now)
    {
        var user = await CreateEssentialUserAsync(db, "+5511999990001", "Teste", now);
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, User = user,
            SymptomCheckinEnabled = true, SymptomCheckinTime = new TimeOnly(9, 0), TimeZone = "America/Sao_Paulo" });
        await db.SaveChangesAsync();
        return user;
    }
    [Fact]
    public async Task RunDueNotificationsAsync_SendsContraceptiveTemplateWithConfiguredReminderTime()
    {
        await using var db = CreateDbContext();
        var sender = new CapturingNotificationSender();
        var now = new DateTimeOffset(2026, 5, 10, 23, 15, 0, TimeSpan.Zero);
        var user = await CreateEssentialUserAsync(db, "+5516992330309", "Nay", now);
        db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            ContraceptiveType = "pill"
        });
        db.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = user.Id,
            ContraceptiveReminderEnabled = true,
            ReminderTime = new TimeOnly(9, 0),
            PeriodReminderTime = new TimeOnly(8, 0),
            ContraceptiveReminderTime = new TimeOnly(20, 15),
            SymptomCheckinTime = new TimeOnly(21, 0),
            TimeZone = "America/Sao_Paulo",
            User = user
        });
        await db.SaveChangesAsync();

        var processed = await CreateProcessor(db, sender, now).RunDueNotificationsAsync();

        Assert.Equal(1, processed);
        var message = Assert.Single(sender.Messages);
        Assert.Equal(NotificationTypes.ContraceptiveDaily, message.TemplateKey);
        Assert.Equal("Nay", message.Variables["1"]);
        Assert.Equal("20:15", message.Variables["2"]);
    }

    [Fact]
    public async Task RunDueNotificationsAsync_SendsSymptomCheckinWhenEnabled()
    {
        await using var db = CreateDbContext();
        var sender = new CapturingNotificationSender();
        var now = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        var user = await CreateEssentialUserAsync(db, "+5516992330310", "Julia", now);
        db.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = user.Id,
            SymptomCheckinEnabled = true,
            ReminderTime = new TimeOnly(9, 0),
            SymptomCheckinTime = new TimeOnly(9, 0),
            TimeZone = "America/Sao_Paulo",
            User = user
        });
        await db.SaveChangesAsync();

        var processed = await CreateProcessor(db, sender, now).RunDueNotificationsAsync();

        Assert.Equal(1, processed);
        var message = Assert.Single(sender.Messages);
        Assert.Equal(NotificationTypes.SymptomCheckin, message.TemplateKey);
        Assert.Equal("Julia", message.Variables["1"]);
    }

    [Fact]
    public async Task RunDueNotificationsAsync_SendsWithinReminderToleranceWindow()
    {
        await using var db = CreateDbContext();
        var sender = new CapturingNotificationSender();
        var now = new DateTimeOffset(2026, 5, 10, 22, 43, 0, TimeSpan.Zero);
        var user = await CreateEssentialUserAsync(db, "+5516992330311", "Nay", now);
        db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            ContraceptiveType = "pill"
        });
        db.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = user.Id,
            ContraceptiveReminderEnabled = true,
            ReminderTime = new TimeOnly(9, 0),
            ContraceptiveReminderTime = new TimeOnly(19, 41),
            TimeZone = "America/Sao_Paulo",
            User = user
        });
        await db.SaveChangesAsync();

        var processed = await CreateProcessor(db, sender, now).RunDueNotificationsAsync();

        Assert.Equal(1, processed);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task RunDueNotificationsAsync_UsesIndependentReminderTimes()
    {
        await using var db = CreateDbContext();
        var sender = new CapturingNotificationSender();
        var now = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        var user = await CreateEssentialUserAsync(db, "+5516992330312", "Marina", now);
        db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            ContraceptiveType = "pill"
        });
        db.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = user.Id,
            ContraceptiveReminderEnabled = true,
            SymptomCheckinEnabled = true,
            ContraceptiveReminderTime = new TimeOnly(7, 0),
            SymptomCheckinTime = new TimeOnly(9, 0),
            TimeZone = "America/Sao_Paulo",
            User = user
        });
        await db.SaveChangesAsync();

        var processed = await CreateProcessor(db, sender, now).RunDueNotificationsAsync();

        Assert.Equal(1, processed);
        var message = Assert.Single(sender.Messages);
        Assert.Equal(NotificationTypes.SymptomCheckin, message.TemplateKey);
    }

    private static LumaDbContext CreateDbContext()
    {
        PrivacyRuntime.Reset();
        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LumaDbContext(options);
    }

    private static NotificationProcessor CreateProcessor(LumaDbContext db, CapturingNotificationSender sender, DateTimeOffset now)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var redis = new RedisConnectionProvider(configuration, NullLogger<RedisConnectionProvider>.Instance);
        return new NotificationProcessor(db, redis, sender, new FixedDateProvider(now), NullLogger<NotificationProcessor>.Instance);
    }

    private static async Task<LumaUser> CreateEssentialUserAsync(LumaDbContext db, string phone, string displayName, DateTimeOffset now)
    {
        var account = new AccountUser
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Cpf = "12345678909",
            FullName = displayName,
            PhoneNumber = phone,
            PasswordHash = "hash"
        };
        db.AccountUsers.Add(account);

        var user = new LumaUser
        {
            PhoneNumber = phone,
            DisplayName = displayName,
            OnboardingStep = OnboardingSteps.Completed,
            ConsentAcceptedAt = now
        };
        db.Users.Add(user);

        db.AccountSubscriptions.Add(new AccountSubscription
        {
            AccountUserId = account.Id,
            PhoneNumber = phone,
            PlanCode = "essencial",
            Status = SubscriptionStatuses.Active,
            CurrentPeriodEndsAt = now.AddDays(20)
        });

        await db.SaveChangesAsync();
        return user;
    }

    private sealed class FixedDateProvider(DateTimeOffset now) : IDateProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class CapturingNotificationSender : IWhatsAppNotificationSender
    {
        public Queue<NotificationSendResult> Results { get; } = new();
        public NotificationProviderStatus? ProviderStatus { get; init; }
        public int StatusLookups { get; private set; }
        public Task<NotificationProviderStatus?> GetMessageStatusAsync(string messageSid, CancellationToken cancellationToken = default)
        {
            StatusLookups++;
            return Task.FromResult(ProviderStatus);
        }
        public List<CapturedMessage> Messages { get; } = [];

        public Task<NotificationSendResult> SendTemplateAsync(string to, string templateKey, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
        {
            Messages.Add(new CapturedMessage(to, templateKey, variables.ToDictionary()));
            if (Results.TryDequeue(out var result)) return Task.FromResult(result);
            return Task.FromResult(new NotificationSendResult(true, $"SM{Guid.NewGuid():N}", null));
        }
    }

    private sealed record CapturedMessage(string To, string TemplateKey, Dictionary<string, string> Variables);
}
