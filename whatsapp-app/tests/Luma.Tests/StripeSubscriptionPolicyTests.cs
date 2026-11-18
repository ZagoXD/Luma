using Luma.Api.Services;

namespace Luma.Tests;

public sealed class StripeSubscriptionPolicyTests
{
    private static readonly StripeBillingOptions Options = new()
    {
        BasicMonthlyPriceId = "basic_month",
        BasicAnnualPriceId = "basic_year",
        EssentialMonthlyPriceId = "essential_month",
        EssentialAnnualPriceId = "essential_year"
    };

    [Theory]
    [InlineData("basic_month", "basico", "monthly", true)]
    [InlineData("basic_year", "basico", "annual", true)]
    [InlineData("essential_month", "essencial", "monthly", true)]
    [InlineData("essential_year", "essencial", "annual", true)]
    [InlineData("basic_month", "essencial", "monthly", false)]
    [InlineData("essential_month", "essencial", "annual", false)]
    [InlineData("unknown", "essencial", "annual", false)]
    [InlineData(null, "essencial", "annual", false)]
    public void Confirmation_must_match_the_price_actually_purchased(
        string? priceId, string plan, string interval, bool expected)
    {
        Assert.Equal(expected, StripeSubscriptionPolicy.MatchesPurchasedPrice(priceId, plan, interval, Options));
    }

    [Theory]
    [InlineData("active", false)]
    [InlineData("canceled", false)]
    [InlineData("pending", true)]
    [InlineData(null, true)]
    public void Replayed_cancellation_preserves_previously_paid_access(string? previousStatus, bool expected)
    {
        Assert.Equal(expected, StripeSubscriptionPolicy.ShouldExpireCanceledSubscription(previousStatus));
    }
}
