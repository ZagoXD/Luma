namespace Luma.Api.Models;

public sealed class LumaUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PhoneNumber { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool? IsAdultConfirmed { get; set; }
    public string OnboardingStep { get; set; } = OnboardingSteps.AwaitingConsent;
    public string? PendingAction { get; set; }
    public DateTimeOffset? ConsentAcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public UserPreference? Preference { get; set; }
}

public static class OnboardingSteps
{
    public const string AwaitingConsent = "awaiting_consent";
    public const string AwaitingDisplayName = "awaiting_display_name";
    public const string AwaitingAgeConfirmation = "awaiting_age_confirmation";
    public const string AwaitingLastPeriodStart = "awaiting_last_period_start";
    public const string AwaitingAverageCycleLength = "awaiting_average_cycle_length";
    public const string AwaitingAveragePeriodLength = "awaiting_average_period_length";
    public const string AwaitingContraceptiveMethod = "awaiting_contraceptive_method";
    public const string Completed = "completed";
    public const string ConsentDeclined = "consent_declined";
    public const string UnderageBlocked = "underage_blocked";
}

public static class PendingActions
{
    public const string AwaitingFlowIntensity = "awaiting_flow_intensity";
    public const string AwaitingPregnancyReference = "awaiting_pregnancy_reference";
}
