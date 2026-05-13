namespace Luma.Api.Models;

public sealed class AccountUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string EmailHash { get; set; } = string.Empty;
    public string Cpf { get; set; } = string.Empty;
    public string CpfHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneHash { get; set; } = string.Empty;
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public string? StripeCustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<AccountSession> Sessions { get; set; } = [];
    public List<AccountSubscription> Subscriptions { get; set; } = [];
    public List<AccountPhoneVerificationCode> PhoneVerificationCodes { get; set; } = [];
    public List<PasswordResetToken> PasswordResetTokens { get; set; } = [];
}

public sealed class AccountPhoneVerificationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountUserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneHash { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AccountUser? AccountUser { get; set; }
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
    public string PhoneHash { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string BillingInterval { get; set; } = "monthly";
    public string Status { get; set; } = SubscriptionStatuses.Active;
    public string? StripeSubscriptionId { get; set; }
    public string? StripePriceId { get; set; }
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

public sealed class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? RequestIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AccountUser? AccountUser { get; set; }
}
