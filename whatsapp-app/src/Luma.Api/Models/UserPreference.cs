namespace Luma.Api.Models;

public sealed class UserPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly? LastPeriodStartDate { get; set; }
    public int AverageCycleLength { get; set; } = 28;
    public int AveragePeriodLength { get; set; } = 5;
    public bool UsesHormonalContraceptive { get; set; }
    public string? ContraceptiveType { get; set; }
    public bool RemindersEnabled { get; set; }
    public string Language { get; set; } = "pt-BR";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public LumaUser? User { get; set; }
}
