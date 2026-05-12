using Luma.Api.Services;

namespace Luma.Tests;

public sealed class BillingPlanCatalogTests
{
    [Theory]
    [InlineData("basico", "monthly", "price_basic_monthly")]
    [InlineData("basico", "annual", "price_basic_annual")]
    [InlineData("essencial", "monthly", "price_essential_monthly")]
    [InlineData("essencial", "annual", "price_essential_annual")]
    public void ResolvePriceId_ReturnsConfiguredPriceForPlanAndInterval(string planCode, string billingInterval, string expectedPrice)
    {
        var options = CreateOptions();

        var priceId = BillingPlanCatalog.ResolvePriceId(planCode, billingInterval, options);

        Assert.Equal(expectedPrice, priceId);
    }

    [Theory]
    [InlineData(" mensal ", "monthly")]
    [InlineData("month", "monthly")]
    [InlineData("monthly", "monthly")]
    [InlineData(" anual ", "annual")]
    [InlineData("year", "annual")]
    [InlineData("annual", "annual")]
    public void NormalizeBillingInterval_AcceptsExpectedAliases(string value, string expected)
    {
        Assert.Equal(expected, BillingPlanCatalog.NormalizeBillingInterval(value));
    }

    [Fact]
    public void ResolvePlanFromPriceId_RecognizesConfiguredPrices()
    {
        var options = CreateOptions();

        var result = BillingPlanCatalog.ResolvePlanFromPriceId("price_essential_annual", options);

        Assert.Equal("essencial", result.PlanCode);
        Assert.Equal("annual", result.BillingInterval);
    }

    [Fact]
    public void ResolvePriceId_FallsBackToLegacyPriceIdsForMonthlyPlans()
    {
        var options = new StripeBillingOptions
        {
            BasicPriceId = "price_legacy_basic",
            EssentialPriceId = "price_legacy_essential"
        };

        Assert.Equal("price_legacy_basic", BillingPlanCatalog.ResolvePriceId("basico", "monthly", options));
        Assert.Equal("price_legacy_essential", BillingPlanCatalog.ResolvePriceId("essencial", "monthly", options));
    }

    private static StripeBillingOptions CreateOptions()
    {
        return new StripeBillingOptions
        {
            BasicMonthlyPriceId = "price_basic_monthly",
            BasicAnnualPriceId = "price_basic_annual",
            EssentialMonthlyPriceId = "price_essential_monthly",
            EssentialAnnualPriceId = "price_essential_annual"
        };
    }
}
