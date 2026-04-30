namespace Luma.Api.Models;

public sealed class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public bool PeriodReminderEnabled { get; set; }
    public bool ContraceptiveReminderEnabled { get; set; }
    public bool SymptomCheckinEnabled { get; set; }
    public TimeOnly ReminderTime { get; set; } = new(9, 0);
    public string TimeZone { get; set; } = "America/Sao_Paulo";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public LumaUser? User { get; set; }
}

public sealed class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? AccountSubscriptionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateOnly ScheduledForDate { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string Status { get; set; } = NotificationDeliveryStatuses.Pending;
    public string? Provider { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public LumaUser? User { get; set; }
}

public sealed class BlockedConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class NotificationTypes
{
    public const string PeriodExpectedTomorrow = "period_expected_tomorrow";
    public const string PeriodExpectedToday = "period_expected_today";
    public const string ContraceptiveDaily = "contraceptive_daily";
    public const string SymptomCheckin = "symptom_checkin";
}

public static class NotificationDeliveryStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}
