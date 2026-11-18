using Luma.Api.Models;

namespace Luma.Api.Services;

public static class StripeSubscriptionPolicy
{
    public static bool MatchesPurchasedPrice(
        string? priceId, string planCode, string billingInterval, StripeBillingOptions options)
    {
        var purchased = BillingPlanCatalog.ResolvePlanFromPriceId(priceId, options);
        return purchased.PlanCode == planCode && purchased.BillingInterval == billingInterval;
    }

    public static bool ShouldExpireCanceledSubscription(string? previousStatus)
    {
        return previousStatus is not (SubscriptionStatuses.Active or SubscriptionStatuses.Canceled);
    }
}
