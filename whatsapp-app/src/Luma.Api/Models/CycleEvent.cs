namespace Luma.Api.Models;

public sealed class CycleEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? CycleId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string Source { get; set; } = "whatsapp";
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class CycleEventTypes
{
    public const string PeriodStart = "period_start";
    public const string PeriodEnd = "period_end";
    public const string FlowUpdate = "flow_update";
    public const string Symptom = "symptom";
    public const string Mood = "mood";
    public const string SexualActivity = "sexual_activity";
    public const string Note = "note";
}
