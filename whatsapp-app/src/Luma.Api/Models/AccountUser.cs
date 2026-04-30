namespace Luma.Api.Models;

public sealed class AccountUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<AccountSession> Sessions { get; set; } = [];
    public List<AccountSubscription> Subscriptions { get; set; } = [];
}

public sealed class AccountSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AccountUser? AccountUser { get; set; }
}

public sealed class AccountSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountUserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string Status { get; set; } = SubscriptionStatuses.Active;
    public string? StripeSubscriptionId { get; set; }
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CurrentPeriodEndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AccountUser? AccountUser { get; set; }
}

public static class SubscriptionStatuses
{
    public const string Active = "active";
    public const string Canceled = "canceled";
    public const string Pending = "pending";
}
