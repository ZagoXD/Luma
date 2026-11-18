namespace Luma.Api.Services;

public static class BillingIntervals
{
    public const string Monthly = "monthly";
    public const string Annual = "annual";
}

public static class BillingPlanCatalog
{
    public static string? NormalizeBillingInterval(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "monthly" or "month" or "mensal" => BillingIntervals.Monthly,
        "annual" or "year" or "yearly" or "anual" => BillingIntervals.Annual,
        _ => null
    };
}
