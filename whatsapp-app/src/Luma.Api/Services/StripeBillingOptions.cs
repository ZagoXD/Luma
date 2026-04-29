namespace Luma.Api.Services;

public sealed class StripeBillingOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string BasicPriceId { get; set; } = string.Empty;
    public string EssentialPriceId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
