using Luma.Api.Models;

namespace Luma.Api.Billing;

public sealed class BillingException(string message, bool uncertain = false) : Exception(message)
{
    public bool Uncertain { get; } = uncertain;
}

public sealed record BillingPlan(string Code, string Interval, decimal Amount, string Name)
{
    public int Months => Interval == "annual" ? 12 : 1;
    public DateTimeOffset PeriodEnd(DateTimeOffset start) => start.AddMonths(Months);
}

public static class BillingCatalog
{
    public static BillingPlan Get(string? code, string? interval) => (code, interval) switch
    {
        ("basico", "monthly") => new("basico", "monthly", 11m, "Luma Básico mensal"),
        ("basico", "annual") => new("basico", "annual", 70.80m, "Luma Básico anual"),
        ("essencial", "monthly") => new("essencial", "monthly", 20m, "Luma Essencial mensal"),
        ("essencial", "annual") => new("essencial", "annual", 118.80m, "Luma Essencial anual"),
        _ => throw new BillingException("Plano ou período de cobrança inválido.")
    };
}

public sealed record ProviderSubscription(string Id, string Reference, string Status, decimal Amount, string Currency, int Months, DateTimeOffset? NextPaymentAt, DateTimeOffset? StartsAt = null);
public sealed record ProviderPayment(string Id, string SubscriptionId, string Status, decimal Amount, string Currency, DateTimeOffset ApprovedAt);
public sealed record SubscriptionOrder(Guid Id, string Email, BillingPlan Plan, string ReturnUrl, DateTimeOffset? StartAt = null);

// The application accepts only a single-use token. PAN, expiration and CVV never enter these contracts.
public interface ISubscriptionGateway
{
    string Provider { get; }
    string PublicKey { get; }
    bool IsReady { get; }
    Task<ProviderSubscription> CreateAsync(SubscriptionOrder order, string cardToken, CancellationToken ct);
    Task<ProviderSubscription> GetAsync(string id, CancellationToken ct);
    Task<ProviderSubscription?> FindAsync(Guid reference, string paymentMethod, CancellationToken ct);
    Task SetRenewalAsync(string id, bool enabled, CancellationToken ct);
    Task ChangePlanAsync(string id, BillingPlan plan, DateTimeOffset nextPaymentAt, CancellationToken ct);
    Task ChangeCardAsync(string id, string cardToken, CancellationToken ct);
    Task CancelPendingAsync(string id, CancellationToken ct);
    Task TerminateAsync(string id, CancellationToken ct);
    Task RefundAsync(string paymentId, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<ProviderPayment>> GetPaymentsAsync(string subscriptionId, CancellationToken ct);
    Task<string?> ResolveSubscriptionAsync(string topic, string resourceId, CancellationToken ct);
    bool VerifyWebhook(string resourceId, string requestId, string signature, DateTimeOffset now);
}

public static class BillingPolicy
{
    public static bool Matches(AccountSubscription local, ProviderSubscription remote)
    {
        var plan = BillingCatalog.Get(local.PlanCode, local.BillingInterval);
        return remote.Reference == local.Id.ToString("N") && remote.Currency == "BRL"
            && remote.Amount == plan.Amount && remote.Months == plan.Months;
    }

    public static bool IsPaid(AccountSubscription local, ProviderSubscription remote, ProviderPayment payment)
        => Matches(local, remote) && payment.SubscriptionId == remote.Id && payment.Status == "approved"
            && payment.Currency == "BRL" && payment.Amount == BillingCatalog.Get(local.PlanCode, local.BillingInterval).Amount;
}
