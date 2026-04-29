namespace Luma.Api.Services;

public sealed class ConversationContext
{
    public string? DisplayName { get; init; }
    public string OnboardingStep { get; init; } = string.Empty;
    public string? PendingAction { get; init; }
    public bool HasCompletedOnboarding { get; init; }
    public bool HasAcceptedConsent { get; init; }
}
