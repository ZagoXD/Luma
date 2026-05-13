namespace Luma.Api.Models;

public sealed class EmailLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string To { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string Provider { get; set; } = "resend";
    public string? ProviderMessageId { get; set; }
    public string Status { get; set; } = EmailLogStatuses.Pending;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class EmailLogStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}
