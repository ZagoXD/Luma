namespace Luma.Api.Models;

public sealed class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Direction { get; set; } = "inbound";
    public string Provider { get; set; } = "twilio";
    public string? ProviderMessageId { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
