namespace Luma.Api.Models;

public sealed class ConsentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string ConsentType { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Version { get; set; } = "mvp-2026-04-25";
}
