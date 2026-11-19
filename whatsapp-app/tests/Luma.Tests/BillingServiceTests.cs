using Luma.Api.Billing;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Luma.Tests;

public sealed class BillingServiceTests
{
    private readonly LumaDbContext db = new(new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Gateway gateway = new();
    private readonly AccountUser account = new() { Email = "buyer@example.com", PhoneNumber = "+5516999999999", PhoneVerifiedAt = DateTimeOffset.UtcNow };
    private BillingService Service => new(db, gateway, new ConfigurationBuilder().Build(), new Emails(), NullLogger<BillingService>.Instance);

    private async Task<AccountSubscription> Prepare()
    {
        if (!await db.AccountUsers.AnyAsync()) { db.AccountUsers.Add(account); await db.SaveChangesAsync(); }
        return await Service.PrepareAsync(account, "essencial", "annual", default);
    }

    [Fact]
    public async Task OpeningCheckoutDoesNotCallProviderOrGrantAccess()
    {
        var pending = await Prepare();
        Assert.Equal("pending", pending.Status);
        Assert.Null(pending.ProviderSubscriptionId);
        Assert.Equal(0, gateway.Creates);
        Assert.Equal(pending.Id, (await Prepare()).Id);
    }

    [PostgresFact]
    public async Task ConcurrentSubmissionsHaveOneRemoteAuthorizationAndOnePaidPeriod()
    {
        PrivacyRuntime.Reset();
        var schema = "billing_test_" + Guid.NewGuid().ToString("N");
        var connectionString = Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")!;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var options = new DbContextOptionsBuilder<LumaDbContext>().UseNpgsql(builder.ConnectionString).Options;
            await using var setup = new LumaDbContext(options);
            await setup.Database.ExecuteSqlRawAsync(setup.Database.GenerateCreateScript());
            await BillingSchema.EnsureAsync(setup);
            setup.AccountUsers.Add(account);
            await setup.SaveChangesAsync();
            BillingService Create(LumaDbContext context) => new(context, gateway, new ConfigurationBuilder().Build(), new Emails(), NullLogger<BillingService>.Instance);
            var pending = await Create(setup).PrepareAsync(account, "essencial", "annual", default);
            gateway.PaymentStatus = "approved";
            await using var first = new LumaDbContext(options);
            await using var second = new LumaDbContext(options);
            await Task.WhenAll(Create(first).SubmitAsync(account, pending.Id, "token", "card", null, default),
                Create(second).SubmitAsync(account, pending.Id, "token", "card", null, default));
            Assert.Equal(1, gateway.Creates);
            Assert.Equal(1, await setup.BillingPayments.CountAsync());
            Assert.Equal("active", await setup.AccountSubscriptions.AsNoTracking().Where(s => s.Id == pending.Id).Select(s => s.Status).SingleAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AuthorizedSubscriptionWithoutPaidInvoiceRemainsPending()
    {
        var pending = await Prepare();
        var result = await Service.SubmitAsync(account, pending.Id, "opaque-token", "card", null, default);
        Assert.Equal("pending", result.Status);
        Assert.Empty(db.BillingPayments);
    }

    [Fact]
    public async Task ConfirmationIsIdempotentAndRefundRevokesPaidAccess()
    {
        var pending = await Prepare();
        gateway.PaymentStatus = "approved";
        var result = await Service.SubmitAsync(account, pending.Id, "opaque-token", "card", null, default);
        var originalEnd = result.CurrentPeriodEndsAt;
        Assert.Equal("active", result.Status);
        await Service.RefreshAsync(account.Id, pending.Id, default);
        await Service.SubmitAsync(account, pending.Id, "opaque-token", "card", null, default);
        Assert.Equal(1, gateway.Creates);
        Assert.Single(db.BillingPayments);
        Assert.Equal(originalEnd, result.CurrentPeriodEndsAt);
        gateway.PaymentStatus = "refunded";
        await Service.RefreshAsync(account.Id, pending.Id, default);
        Assert.Equal("pending", result.Status);
        Assert.True(result.CurrentPeriodEndsAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AnotherAccountCannotPayOrReadMyOrder()
    {
        var pending = await Prepare();
        await Assert.ThrowsAsync<BillingException>(() => Service.SubmitAsync(new AccountUser(), pending.Id, "token", "card", null, default));
        await Assert.ThrowsAsync<BillingException>(() => Service.RefreshAsync(Guid.NewGuid(), pending.Id, default));
        Assert.Equal(0, gateway.Creates);
    }

    [Fact]
    public async Task ActiveAccountCannotOpenAnotherSubscription()
    {
        var pending = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, pending.Id, "token", "card", null, default);
        await Assert.ThrowsAsync<BillingException>(() => Service.PrepareAsync(account, "basico", "monthly", default));
    }

    [Fact]
    public async Task CancelStopsRenewalButPreservesPaidPeriodAndCanResume()
    {
        var pending = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, pending.Id, "token", "card", null, default);
        var end = pending.CurrentPeriodEndsAt;
        await Service.SetRenewalAsync(account.Id, false, default);
        Assert.Equal("paused", gateway.Remote!.Status);
        Assert.Equal("canceled", pending.Status);
        Assert.Equal(end, pending.CurrentPeriodEndsAt);
        await Service.SetRenewalAsync(account.Id, true, default);
        Assert.Equal("active", pending.Status);
        Assert.Equal(end, pending.CurrentPeriodEndsAt);
    }

    [Fact]
    public async Task ChangingPlanDoesNotChangePaidEntitlementBeforeNextPayment()
    {
        var pending = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, pending.Id, "token", "card", null, default);
        await Service.ChangePlanAsync(account.Id, "basico", "annual", default);
        Assert.Equal("essencial", pending.PlanCode);
        Assert.Equal("basico", pending.PendingPlanCode);
        await Service.RefreshAsync(account.Id, pending.Id, default);
        Assert.Equal("essencial", pending.PlanCode);
    }

    [Fact]
    public async Task ChangingFrequencyRequiresNewAuthorizationRatherThanAnUnsupportedUpdate()
    {
        var pending = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, pending.Id, "token", "card", null, default);
        await Assert.ThrowsAsync<BillingException>(() => Service.ChangePlanAsync(account.Id, "essencial", "monthly", default));
        var replacement = await Service.PrepareAsync(account, "essencial", "monthly", default, pending.Id);
        Assert.Equal(pending.CurrentPeriodEndsAt, replacement.StartsAt);
        Assert.Equal(pending.Id, replacement.ReplacesSubscriptionId);
        Assert.Equal("active", pending.Status);
        Assert.Equal(1, gateway.Creates);
    }

    [Fact]
    public async Task PixIsRejectedWithoutCallingTheProvider()
    {
        var pending = await Prepare();
        await Assert.ThrowsAsync<BillingException>(() => Service.SubmitAsync(account, pending.Id, null, "pix", null, default));
        Assert.Equal(0, gateway.Creates);
    }

    [Fact]
    public async Task ImmediateCancellationCannotBeResurrectedByWebhookOrRenewal()
    {
        var subscription = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, subscription.Id, "token", "card", null, default);
        var paidEnd = subscription.CurrentPeriodEndsAt;
        await Service.TerminateAsync(account.Id, subscription.Id, false, default);
        await Service.RefreshAsync(account.Id, subscription.Id, default);
        Assert.Equal("terminated", subscription.Status);
        Assert.Equal(paidEnd, subscription.CurrentPeriodEndsAt);
        await Assert.ThrowsAsync<BillingException>(() => Service.SetRenewalAsync(account.Id, true, default));
    }

    [Fact]
    public async Task AccountDeletionRequiresPasswordAndErasesHealthAndContactDetails()
    {
        account.PasswordHash = AccountSecurity.HashPassword("correct-password");
        var subscription = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, subscription.Id, "token", "card", null, default);
        var user = new LumaUser { PhoneNumber = account.PhoneNumber };
        db.Users.Add(user);
        db.UserPreferences.Add(new() { UserId = user.Id });
        db.Cycles.Add(new() { UserId = user.Id });
        await db.SaveChangesAsync();
        var deletion = new AccountDeletionService(db, Service);
        await Assert.ThrowsAsync<BillingException>(() => deletion.RequestAsync(account.Id, "wrong", default));
        Assert.Null(account.DeletionRequestedAt);
        await deletion.RequestAsync(account.Id, "correct-password", default);
        Assert.Empty(db.Users);
        Assert.Empty(db.Cycles);
        Assert.Empty(db.UserPreferences);
        Assert.NotNull(account.DeletionCompletedAt);
        Assert.EndsWith("@deleted.invalid", account.Email);
        Assert.Equal("terminated", subscription.Status);
        Assert.Single(db.BillingPayments);
    }

    [Fact]
    public async Task WithdrawalRefundsOnceAndPreservesItsRequestAfterFailure()
    {
        var subscription = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, subscription.Id, "token", "card", null, default);
        gateway.FailRefund = true;
        await Assert.ThrowsAsync<BillingException>(() => Service.TerminateAsync(account.Id, subscription.Id, true, default));
        Assert.NotNull(subscription.WithdrawalRequestedAt);
        Assert.Equal("terminated", subscription.Status);
        gateway.FailRefund = false;
        await Service.TerminateAsync(account.Id, subscription.Id, true, default);
        await Service.TerminateAsync(account.Id, subscription.Id, true, default);
        Assert.Equal(1, gateway.Refunds);
        Assert.NotNull(subscription.WithdrawalCompletedAt);
    }

    [Fact]
    public async Task LateWithdrawalDoesNotCancelAnOtherwiseValidSubscription()
    {
        var subscription = await Prepare();
        gateway.PaymentStatus = "approved";
        await Service.SubmitAsync(account, subscription.Id, "token", "card", null, default);
        db.BillingPayments.Single().ApprovedAt = DateTimeOffset.UtcNow.AddDays(-8);
        subscription.CreatedAt = DateTimeOffset.UtcNow.AddDays(-8);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BillingException>(() => Service.TerminateAsync(account.Id, subscription.Id, true, default));
        Assert.Equal("active", subscription.Status);
    }

    [Fact]
    public async Task PendingAuthorizationCanBeStoppedBeforeFirstCharge()
    {
        var pending = await Prepare();
        await Service.SubmitAsync(account, pending.Id, "token", "card", null, default);
        await Service.CancelScheduledAsync(account.Id, pending.Id, default);
        Assert.Equal("failed", pending.Status);
        Assert.Equal("paused", gateway.Remote!.Status);
        Assert.Empty(db.BillingPayments);
    }

    [Fact]
    public async Task FailedScheduledOrderMustNeverBePresentedAsAPaidConfirmation()
    {
        var pending = await Prepare();
        pending.Status = "failed";
        pending.StartsAt = DateTimeOffset.UtcNow.AddMonths(1);
        pending.CurrentPeriodEndsAt = pending.StartsAt;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BillingException>(() => Service.SubmitAsync(account, pending.Id, "token", "card", null, default));
        Assert.Equal(0, gateway.Creates);
    }

    [Fact]
    public async Task AmountManipulationCannotGrantAccess()
    {
        var pending = await Prepare();
        gateway.PaymentStatus = "approved";
        gateway.PaymentAmount = 1;
        var result = await Service.SubmitAsync(account, pending.Id, "token", "card", null, default);
        Assert.Equal("pending", result.Status);
        Assert.Empty(db.BillingPayments);
    }

    private sealed class Gateway : ISubscriptionGateway
    {
        public string Provider => "mercadopago";
        public string PublicKey => "public";
        public bool IsReady => true;
        public ProviderSubscription? Remote;
        public int Creates;
        public int Refunds;
        public bool FailRefund;
        public string PaymentStatus = "pending";
        public decimal PaymentAmount = 118.80m;
        public Task<ProviderSubscription> CreateAsync(SubscriptionOrder order, string token, CancellationToken ct)
        {
            Creates++;
            Remote = new("remote", order.Id.ToString("N"), "authorized", order.Plan.Amount, "BRL", order.Plan.Months, null);
            return Task.FromResult(Remote);
        }
        public Task<ProviderSubscription> GetAsync(string id, CancellationToken ct) => Task.FromResult(Remote!);
        public Task<ProviderSubscription?> FindAsync(Guid reference, string method, CancellationToken ct) => Task.FromResult(Remote);
        public Task SetRenewalAsync(string id, bool enabled, CancellationToken ct) { Remote = Remote! with { Status = enabled ? "authorized" : "paused" }; return Task.CompletedTask; }
        public Task ChangePlanAsync(string id, BillingPlan plan, DateTimeOffset date, CancellationToken ct) { Remote = Remote! with { Amount = plan.Amount, Months = plan.Months }; return Task.CompletedTask; }
        public Task ChangeCardAsync(string id, string token, CancellationToken ct) => Task.CompletedTask;
        public Task CancelPendingAsync(string id, CancellationToken ct) => SetRenewalAsync(id, false, ct);
        public Task TerminateAsync(string id, CancellationToken ct) { Remote = Remote! with { Status = "cancelled" }; return Task.CompletedTask; }
        public Task RefundAsync(string paymentId, string key, CancellationToken ct)
        {
            if (FailRefund) throw new BillingException("temporary", true);
            Refunds++; PaymentStatus = "refunded"; return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ProviderPayment>> GetPaymentsAsync(string id, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ProviderPayment>>([new("payment", id, PaymentStatus, PaymentAmount, "BRL", DateTimeOffset.UtcNow)]);
        public Task<string?> ResolveSubscriptionAsync(string topic, string resource, CancellationToken ct) => Task.FromResult<string?>("remote");
        public bool VerifyWebhook(string id, string request, string signature, DateTimeOffset now) => true;
    }

    private sealed class Emails : IEmailService
    {
        private static Task<EmailSendResult> Sent => Task.FromResult(new EmailSendResult(true, null, null));
        public Task<EmailSendResult> SendWelcomeEmailAsync(string to, string? name, CancellationToken ct = default) => Sent;
        public Task<EmailSendResult> SendSubscriptionCreatedEmailAsync(string to, string? name, string plan, CancellationToken ct = default) => Sent;
        public Task<EmailSendResult> SendPasswordResetEmailAsync(string to, string? name, string url, int minutes, CancellationToken ct = default) => Sent;
        public Task<EmailSendResult> SendSupportRequestToAdminAsync(SupportRequest request, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default) => Sent;
        public Task<EmailSendResult> SendSupportRequestConfirmationToUserAsync(SupportRequest request, CancellationToken ct = default) => Sent;
    }
}
