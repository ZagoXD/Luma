using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class NotificationProcessorTests
{
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
        public List<CapturedMessage> Messages { get; } = [];

        public Task<NotificationSendResult> SendTemplateAsync(string to, string templateKey, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
        {
            Messages.Add(new CapturedMessage(to, templateKey, variables.ToDictionary()));
            return Task.FromResult(new NotificationSendResult(true, $"SM{Guid.NewGuid():N}", null));
        }
    }

    private sealed record CapturedMessage(string To, string TemplateKey, Dictionary<string, string> Variables);
}
