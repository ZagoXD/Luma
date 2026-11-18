using System.Security.Cryptography;
using System.Text;
using Luma.Api.Billing;
using Luma.Api.Models;

namespace Luma.Tests;

public class BillingPolicyTests
{
    [Theory]
    [InlineData("basico", "monthly", 11)]
    [InlineData("basico", "annual", 70.80)]
    [InlineData("essencial", "monthly", 20)]
    [InlineData("essencial", "annual", 118.80)]
    public void PricesAreAuthoritative(string plan, string interval, decimal amount)
        => Assert.Equal(amount, BillingCatalog.Get(plan, interval).Amount);

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
