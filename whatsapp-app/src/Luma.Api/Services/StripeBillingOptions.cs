namespace Luma.Api.Services;

public sealed class StripeBillingOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string BasicPriceId { get; set; } = string.Empty;
    public string EssentialPriceId { get; set; } = string.Empty;
    public string BasicMonthlyPriceId { get; set; } = string.Empty;
    public string BasicAnnualPriceId { get; set; } = string.Empty;
    public string EssentialMonthlyPriceId { get; set; } = string.Empty;
    public string EssentialAnnualPriceId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}

public static class BillingIntervals
{
    public const string Monthly = "monthly";
    public const string Annual = "annual";
}

public static class BillingPlanCatalog
{
    public static string? NormalizeBillingInterval(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "monthly" or "month" or "mensal" => BillingIntervals.Monthly,
            "annual" or "year" or "yearly" or "anual" => BillingIntervals.Annual,
            _ => null
        };
    }

    public static string ResolvePriceId(string planCode, string billingInterval, StripeBillingOptions options)
    {
        var priceId = (planCode, billingInterval) switch
        {
            ("basico", BillingIntervals.Monthly) => FirstConfigured(options.BasicMonthlyPriceId, options.BasicPriceId),
            ("basico", BillingIntervals.Annual) => options.BasicAnnualPriceId,
            ("essencial", BillingIntervals.Monthly) => FirstConfigured(options.EssentialMonthlyPriceId, options.EssentialPriceId),
            ("essencial", BillingIntervals.Annual) => options.EssentialAnnualPriceId,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(priceId))
        {
            throw new InvalidOperationException($"Configure o Price ID da Stripe para o plano {planCode} no ciclo {billingInterval}.");
        }

        return priceId;
    }

    public static (string? PlanCode, string? BillingInterval) ResolvePlanFromPriceId(string? priceId, StripeBillingOptions options)
    {
        if (string.IsNullOrWhiteSpace(priceId))
        {
            return (null, null);
        }

        if (priceId == options.BasicMonthlyPriceId || priceId == options.BasicPriceId)
        {
            return ("basico", BillingIntervals.Monthly);
        }

        if (priceId == options.BasicAnnualPriceId)
        {
            return ("basico", BillingIntervals.Annual);
        }

        if (priceId == options.EssentialMonthlyPriceId || priceId == options.EssentialPriceId)
        {
            return ("essencial", BillingIntervals.Monthly);
        }

        if (priceId == options.EssentialAnnualPriceId)
        {
            return ("essencial", BillingIntervals.Annual);
        }

        return (null, null);
    }

    private static string FirstConfigured(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
