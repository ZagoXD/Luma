namespace Luma.Api.Models;

public sealed class PendingIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Intent { get; set; } = string.Empty;
    public DateOnly? Date { get; set; }
    public string RequiredBeforeAction { get; set; } = PendingIntentRequirements.FinishOnboarding;
    public string Status { get; set; } = PendingIntentStatus.PendingConfirmation;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class PendingIntentStatus
{
    public const string PendingConfirmation = "pending_confirmation";
    public const string Completed = "completed";
    public const string Dismissed = "dismissed";
}

public static class PendingIntentRequirements
{
    public const string FinishOnboarding = "finish_onboarding";
}
