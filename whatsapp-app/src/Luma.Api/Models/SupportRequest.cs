namespace Luma.Api.Models;

public sealed class SupportRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AttachmentCount { get; set; }
    public string Status { get; set; } = SupportRequestStatuses.Received;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AccountUser? User { get; set; }
    public List<SupportRequestAttachmentMetadata> Attachments { get; set; } = [];
}

public sealed class SupportRequestAttachmentMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupportRequestId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SupportRequest? SupportRequest { get; set; }
}

public static class SupportRequestStatuses
{
    public const string Received = "received";
}
