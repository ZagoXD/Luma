using System.Security.Cryptography;
using System.Text;
using Luma.Api.Billing;
using Luma.Api.Models;

namespace Luma.Tests;

public class BillingPolicyTests
{
    [Theory]
    [InlineData("active", "essencial", 1, true)]
    [InlineData("canceled", "essencial", 1, true)]
    [InlineData("pending", "essencial", 1, false)]
    [InlineData("terminated", "essencial", 1, false)]
    [InlineData("active", "basico", 1, false)]
    [InlineData("active", "essencial", -1, false)]
    public void Essential_entitlement_depends_on_paid_validity_not_automatic_renewal(string status, string plan, int days, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var subscription = new AccountSubscription { Status = status, PlanCode = plan, CurrentPeriodEndsAt = now.AddDays(days) };
        Assert.Equal(expected, BillingPolicy.HasAccess(subscription, now, "essencial"));
        subscription.TerminatedAt = now;
        Assert.False(BillingPolicy.HasAccess(subscription, now, "essencial"));
    }

    [Theory]
    [InlineData("basico", "monthly", 32.90)]
    [InlineData("basico", "annual", 334.80)]
    [InlineData("essencial", "monthly", 59.90)]
    [InlineData("essencial", "annual", 658.80)]
    public void PricesAreAuthoritative(string plan, string interval, decimal amount)
        => Assert.Equal(amount, BillingCatalog.Get(plan, interval).Amount);

    [Fact]
    public void Contract_price_is_immutable_and_lower_payments_cannot_activate_new_contracts()
    {
        var local = new AccountSubscription { PlanCode = "basico", BillingInterval = "monthly", ContractAmount = 32.90m };
        var remote = new ProviderSubscription("r", local.Id.ToString("N"), "authorized", 11m, "BRL", 1, null);
        Assert.False(BillingPolicy.Matches(local, remote));
        local.ContractAmount = 11m;
        Assert.True(BillingPolicy.Matches(local, remote));
    }

    [Fact]
    public void UnknownPlanIsRejected() => Assert.Throws<BillingException>(() => BillingCatalog.Get("premium", "monthly"));

    [Theory]
    [InlineData("authorized", "pending", 118.80, "BRL", false)]
    [InlineData("authorized", "approved", 1, "BRL", false)]
    [InlineData("authorized", "approved", 118.80, "USD", false)]
    [InlineData("authorized", "approved", 118.80, "BRL", true)]
    public void AuthorizationAloneNeverGrantsAccess(string status, string paymentStatus, decimal amount, string currency, bool expected)
    {
        var subscription = new AccountSubscription { Id = Guid.NewGuid(), PlanCode = "essencial", BillingInterval = "annual" };
        var remote = new ProviderSubscription("remote", subscription.Id.ToString("N"), status, 118.80m, "BRL", 12, null);
        var payment = new ProviderPayment("payment", "remote", paymentStatus, amount, currency, DateTimeOffset.UtcNow);
        Assert.Equal(expected, BillingPolicy.IsPaid(subscription, remote, payment));
    }

    [Fact]
    public void AnotherSubscriptionsPaymentCannotActivateAccess()
    {
        var local = new AccountSubscription { PlanCode = "basico", BillingInterval = "monthly" };
        var remote = new ProviderSubscription("mine", local.Id.ToString("N"), "authorized", 11, "BRL", 1, null);
        Assert.False(BillingPolicy.IsPaid(local, remote, new("p", "other", "approved", 11, "BRL", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void AnnualPeriodUsesCalendarYearNot365Days()
        => Assert.Equal(new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), BillingCatalog.Get("basico", "annual").PeriodEnd(new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void WebhookRequiresSecretAndValidSignature()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes($"id:123;request-id:req;ts:{ts};"))).ToLowerInvariant();
        Assert.True(MercadoPagoWebhook.Verify("secret", "123", "req", $"ts={ts},v1={hash}", DateTimeOffset.UtcNow));
        Assert.False(MercadoPagoWebhook.Verify("", "123", "req", $"ts={ts},v1={hash}", DateTimeOffset.UtcNow));
        Assert.False(MercadoPagoWebhook.Verify("secret", "456", "req", $"ts={ts},v1={hash}", DateTimeOffset.UtcNow));
        Assert.False(MercadoPagoWebhook.Verify("secret", "123", "req", $"ts={ts},v1={hash}", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
