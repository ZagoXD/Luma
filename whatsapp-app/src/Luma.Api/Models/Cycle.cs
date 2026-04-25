namespace Luma.Api.Models;

public sealed class Cycle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = CycleStatus.Ongoing;
    public int CycleNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class CycleStatus
{
    public const string Ongoing = "ongoing";
    public const string Finished = "finished";
    public const string Unknown = "unknown";
}
