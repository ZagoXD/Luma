using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Luma.Tests;

public sealed class PostgresNotificationTests
{
    [PostgresFact]
    public async Task Independent_workers_claim_one_submission_and_retry_once_after_a_rejection()
    {
        await WithDatabase(async options => {
            var now = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            await Seed(options, now);
            var sender = new Sender();
            await using (var first = new LumaDbContext(options))
                await Processor(first, sender, now).RunDueNotificationsAsync();
            Assert.Equal(1, sender.Calls);
            await using var a = new LumaDbContext(options);
            await using var b = new LumaDbContext(options);
            await Task.WhenAll(Processor(a, sender, now.AddMinutes(1)).RunDueNotificationsAsync(),
                Processor(b, sender, now.AddMinutes(1)).RunDueNotificationsAsync());
            Assert.Equal(2, sender.Calls);
            await using var check = new LumaDbContext(options);
            var delivery = Assert.Single(await check.NotificationDeliveries.ToListAsync());
            Assert.Equal(2, delivery.Attempts);
            Assert.Equal(NotificationDeliveryStatuses.Submitted, delivery.Status);
        });
    }

    [PostgresFact]
    public async Task Early_delivery_callback_is_not_overwritten_by_the_send_response()
    {
        await WithDatabase(async options => {
            var now = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            await Seed(options, now);
            await using var db = new LumaDbContext(options);
            await Processor(db, new EarlyCallbackSender(options), now).RunDueNotificationsAsync();
            var delivery = Assert.Single(await db.NotificationDeliveries.AsNoTracking().ToListAsync());
            Assert.Equal(NotificationDeliveryStatuses.Delivered, delivery.Status);
            Assert.NotNull(delivery.SentAt);
        });
    }

    private static async Task WithDatabase(Func<DbContextOptions<LumaDbContext>, Task> action)
    {
        PrivacyRuntime.Reset();
        var schema = "luma_notification_test_" + Guid.NewGuid().ToString("N");
        var connectionString = Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var options = new DbContextOptionsBuilder<LumaDbContext>().UseNpgsql(builder.ConnectionString).Options;
            await using (var db = new LumaDbContext(options)) await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            await action(options);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task Seed(DbContextOptions<LumaDbContext> options, DateTimeOffset now)
    {
        await using var db = new LumaDbContext(options);
        var account = new AccountUser { Email = "test@luma.test", Cpf = "52998224725", FullName = "Teste",
            PhoneNumber = "+5511999990001", PasswordHash = "test" };
        var user = new LumaUser { PhoneNumber = account.PhoneNumber, OnboardingStep = OnboardingSteps.Completed, ConsentAcceptedAt = now };
        db.AccountUsers.Add(account); db.Users.Add(user);
        db.AccountSubscriptions.Add(new AccountSubscription { AccountUserId = account.Id, PhoneNumber = account.PhoneNumber,
            PlanCode = "essencial", Status = SubscriptionStatuses.Active, CurrentPeriodEndsAt = now.AddDays(1) });
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user.Id, User = user, SymptomCheckinEnabled = true });
        await db.SaveChangesAsync();
    }

    private static NotificationProcessor Processor(LumaDbContext db, IWhatsAppNotificationSender sender, DateTimeOffset now) => new(db,
        new RedisConnectionProvider(new ConfigurationBuilder().Build(), NullLogger<RedisConnectionProvider>.Instance),
        sender, new DateProvider(now), NullLogger<NotificationProcessor>.Instance);

    private sealed class DateProvider(DateTimeOffset now) : IDateProvider { public DateTimeOffset UtcNow => now; }
    private sealed class Sender : IWhatsAppNotificationSender
    {
        public int Calls;
        public Task<NotificationSendResult> SendTemplateAsync(string to, string template, IReadOnlyDictionary<string, string> variables, CancellationToken ct = default)
            => Task.FromResult(Interlocked.Increment(ref Calls) == 1
                ? new NotificationSendResult(false, null, "429", Retryable: true)
                : new NotificationSendResult(true, "SMtest", null));
    }
    private sealed class EarlyCallbackSender(DbContextOptions<LumaDbContext> options) : IWhatsAppNotificationSender
    {
        public Task<NotificationSendResult> SendTemplateAsync(string to, string template, IReadOnlyDictionary<string, string> variables, CancellationToken ct = default)
            => throw new InvalidOperationException("Expected scheduled send");
        public async Task<NotificationSendResult> SendScheduledTemplateAsync(string to, string template, IReadOnlyDictionary<string, string> variables, Guid id, CancellationToken ct = default)
        {
            await using var callbackDb = new LumaDbContext(options);
            await new NotificationStatusService(callbackDb).ApplyAsync(id, "SMtest", "delivered", null, ct);
            return new NotificationSendResult(true, "SMtest", null);
        }
    }
}
