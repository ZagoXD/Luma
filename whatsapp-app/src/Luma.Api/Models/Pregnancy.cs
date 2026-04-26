namespace Luma.Api.Models;

public sealed class Pregnancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Status { get; set; } = PregnancyStatus.Active;
    public string? StartReference { get; set; }
    public DateOnly? LastPeriodDate { get; set; }
    public int? GestationalWeeksAtRegistration { get; set; }
    public DateOnly? EstimatedDueDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PregnancyStatus
{
    public const string Active = "active";
    public const string Finished = "finished";
    public const string Unknown = "unknown";
}
