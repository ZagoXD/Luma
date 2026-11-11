namespace Luma.Api.Models;

public sealed class AssistantTurnRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string Request { get; set; } = "";
    public string Reply { get; set; } = "";
    public string? MediaUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
