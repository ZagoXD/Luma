using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class ResendOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class EmailOptions
{
    public string From { get; set; } = "Luma <noreply@ia-luma.com.br>";
    public string SupportTo { get; set; } = "lumasuporte.ia@gmail.com";
    public int PasswordResetExpirationMinutes { get; set; } = 30;
    public int MaxSupportAttachments { get; set; } = 3;
    public long MaxSupportAttachmentBytes { get; set; } = 5 * 1024 * 1024;
    public int SupportDailyLimit { get; set; } = 5;
    public string WebBaseUrl { get; set; } = string.Empty;
}

public sealed class EmailTemplateOptions
{
    public string Welcome { get; set; } = string.Empty;
    public string SubscriptionCreated { get; set; } = string.Empty;
    public string PasswordReset { get; set; } = string.Empty;
    public string SupportAdmin { get; set; } = string.Empty;
    public string SupportUserConfirmation { get; set; } = string.Empty;
}

public sealed record EmailSendResult(bool Success, string? ProviderMessageId, string? ErrorMessage);

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public interface IEmailService
{
    Task<EmailSendResult> SendWelcomeEmailAsync(string to, string? userName, CancellationToken cancellationToken = default);

    Task<EmailSendResult> SendSubscriptionCreatedEmailAsync(
        string to,
        string? userName,
        string planName,
        CancellationToken cancellationToken = default);

    Task<EmailSendResult> SendPasswordResetEmailAsync(
        string to,
        string? userName,
        string resetUrl,
        int expiresInMinutes,
        CancellationToken cancellationToken = default);

    Task<EmailSendResult> SendSupportRequestToAdminAsync(
        SupportRequest request,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default);

    Task<EmailSendResult> SendSupportRequestConfirmationToUserAsync(
        SupportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ResendEmailService(
    HttpClient http,
    IOptions<ResendOptions> resendOptions,
    IOptions<EmailOptions> emailOptions,
    IOptions<EmailTemplateOptions> templateOptions,
    ILogger<ResendEmailService> logger) : IEmailService
{
    private readonly ResendOptions _resend = resendOptions.Value;
    private readonly EmailOptions _email = emailOptions.Value;
    private readonly EmailTemplateOptions _templates = templateOptions.Value;

    public Task<EmailSendResult> SendWelcomeEmailAsync(string to, string? userName, CancellationToken cancellationToken = default)
    {
        return SendTemplateAsync(
            to,
            _templates.Welcome,
            new Dictionary<string, object?>
            {
                ["userName"] = DisplayName(userName),
                ["loginUrl"] = BuildWebUrl(_email.WebBaseUrl, "/login")
            },
            cancellationToken);
    }

    public Task<EmailSendResult> SendSubscriptionCreatedEmailAsync(string to, string? userName, string planName, CancellationToken cancellationToken = default)
    {
        return SendTemplateAsync(
            to,
            _templates.SubscriptionCreated,
            new Dictionary<string, object?>
            {
                ["userName"] = DisplayName(userName),
                ["planName"] = planName,
                ["billingUrl"] = BuildWebUrl(_email.WebBaseUrl, "/perfil?tab=billing"),
                ["appUrl"] = BuildWebUrl(_email.WebBaseUrl, "/perfil")
            },
            cancellationToken);
    }

    public Task<EmailSendResult> SendPasswordResetEmailAsync(string to, string? userName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
    {
        return SendTemplateAsync(
            to,
            _templates.PasswordReset,
            new Dictionary<string, object?>
            {
                ["userName"] = DisplayName(userName),
                ["resetUrl"] = resetUrl,
                ["expiresInMinutes"] = expiresInMinutes
            },
            cancellationToken);
    }

    public Task<EmailSendResult> SendSupportRequestToAdminAsync(SupportRequest supportRequest, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken = default)
    {
        return SendTemplateAsync(
            _email.SupportTo,
            _templates.SupportAdmin,
            BuildSupportVariables(supportRequest, includeUserEmail: true),
            cancellationToken,
            replyTo: supportRequest.UserEmail,
            subject: $"Nova solicitação de suporte #{supportRequest.Id}",
            attachments: attachments);
    }

    public Task<EmailSendResult> SendSupportRequestConfirmationToUserAsync(SupportRequest supportRequest, CancellationToken cancellationToken = default)
    {
        return SendTemplateAsync(
            supportRequest.UserEmail,
            _templates.SupportUserConfirmation,
            BuildSupportVariables(supportRequest, includeUserEmail: false),
            cancellationToken,
            subject: "Recebemos sua solicitação de suporte");
    }

    private async Task<EmailSendResult> SendTemplateAsync(
        string to,
        string templateId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken,
        string? replyTo = null,
        string? subject = null,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        if (string.IsNullOrWhiteSpace(_resend.ApiKey)
            || string.IsNullOrWhiteSpace(_email.From)
            || string.IsNullOrWhiteSpace(templateId))
        {
            logger.LogInformation("Resend email skipped because API key, sender or template is not configured.");
            return new EmailSendResult(false, null, "resend_not_configured");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _resend.ApiKey);
        var payload = new Dictionary<string, object?>
        {
            ["from"] = _email.From,
            ["to"] = new[] { to },
            ["template"] = new
            {
                id = templateId,
                variables
            }
        };

        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            payload["reply_to"] = replyTo;
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            payload["subject"] = subject;
        }

        if (attachments is { Count: > 0 })
        {
            payload["attachments"] = attachments.Select(attachment => new
            {
                filename = attachment.FileName,
                content = Convert.ToBase64String(attachment.Content)
            }).ToArray();
        }

        request.Content = JsonContent.Create(payload);

        using var response = await http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new EmailSendResult(false, null, content.Length > 512 ? content[..512] : content);
        }

        using var document = JsonDocument.Parse(content);
        var id = document.RootElement.TryGetProperty("id", out var idProperty)
            ? idProperty.GetString()
            : null;

        return new EmailSendResult(true, id, null);
    }

    public static string BuildWebUrl(string baseUrl, string path)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://localhost:3000"
            : baseUrl.Trim().TrimEnd('/');
        return $"{normalizedBase}/{path.TrimStart('/')}";
    }

    private static string DisplayName(string? userName)
    {
        return string.IsNullOrWhiteSpace(userName) ? "tudo bem?" : userName.Trim();
    }

    private static Dictionary<string, object?> BuildSupportVariables(SupportRequest request, bool includeUserEmail)
    {
        var variables = new Dictionary<string, object?>
        {
            ["USER_NAME"] = request.UserName,
            ["SUPPORT_REQUEST_ID"] = request.Id.ToString(),
            ["SUBJECT"] = request.Subject,
            ["CREATED_AT"] = request.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            ["ATTACHMENT_COUNT"] = request.AttachmentCount.ToString()
        };

        if (includeUserEmail)
        {
            variables["USER_EMAIL"] = request.UserEmail;
            variables["DESCRIPTION"] = request.Description;
        }

        return variables;
    }
}

public sealed record PasswordResetRequestResult(bool Success, string Message);

public sealed record SupportAttachmentInput(string FileName, string ContentType, byte[] Content);

public sealed record SupportRequestResult(bool Success, string Message, SupportRequest? Request = null);

public sealed class SupportRequestService(
    LumaDbContext db,
    IEmailService emailService,
    IOptions<EmailOptions> emailOptions,
    ILogger<SupportRequestService> logger)
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "application/pdf"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".pdf"
    };

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".bat",
        ".cmd",
        ".js",
        ".zip",
        ".rar",
        ".7z",
        ".scr"
    };

    public async Task<SupportRequestResult> CreateAsync(
        AccountUser account,
        string subject,
        string description,
        IReadOnlyList<SupportAttachmentInput> attachments,
        CancellationToken cancellationToken = default)
    {
        var normalizedSubject = subject.Trim();
        var normalizedDescription = description.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSubject))
        {
            return new SupportRequestResult(false, "Informe o assunto da solicitação.");
        }

        if (string.IsNullOrWhiteSpace(normalizedDescription))
        {
            return new SupportRequestResult(false, "Informe a descrição da solicitação.");
        }

        var maxAttachments = Math.Max(0, emailOptions.Value.MaxSupportAttachments);
        if (attachments.Count > maxAttachments)
        {
            return new SupportRequestResult(false, $"Você pode enviar no máximo {maxAttachments} anexos.");
        }

        var maxBytes = Math.Max(1, emailOptions.Value.MaxSupportAttachmentBytes);
        foreach (var attachment in attachments)
        {
            var validationError = ValidateAttachment(attachment, maxBytes);
            if (validationError is not null)
            {
                return new SupportRequestResult(false, validationError);
            }
        }

        var today = DateTimeOffset.UtcNow.AddDays(-1);
        var dailyLimit = Math.Max(1, emailOptions.Value.SupportDailyLimit);
        var requestCount = await db.SupportRequests.CountAsync(
            request => request.UserId == account.Id && request.CreatedAt >= today,
            cancellationToken);
        if (requestCount >= dailyLimit)
        {
            return new SupportRequestResult(false, "Você atingiu o limite de solicitações de suporte por hoje. Tente novamente mais tarde.");
        }

        var supportRequest = new SupportRequest
        {
            UserId = account.Id,
            UserName = account.FullName,
            UserEmail = account.Email,
            Subject = normalizedSubject,
            Description = normalizedDescription,
            AttachmentCount = attachments.Count,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.SupportRequests.Add(supportRequest);
        foreach (var attachment in attachments)
        {
            db.SupportRequestAttachmentMetadata.Add(new SupportRequestAttachmentMetadata
            {
                SupportRequestId = supportRequest.Id,
                FileName = SanitizeFileName(attachment.FileName),
                ContentType = attachment.ContentType,
                SizeBytes = attachment.Content.Length,
                CreatedAt = supportRequest.CreatedAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var emailAttachments = attachments
            .Select(attachment => new EmailAttachment(SanitizeFileName(attachment.FileName), attachment.ContentType, attachment.Content))
            .ToList();
        var adminResult = await emailService.SendSupportRequestToAdminAsync(supportRequest, emailAttachments, cancellationToken);
        if (!adminResult.Success)
        {
            logger.LogWarning("support_request_admin_email_failed for request {SupportRequestId}: {Error}", supportRequest.Id, adminResult.ErrorMessage);
        }

        var userResult = await emailService.SendSupportRequestConfirmationToUserAsync(supportRequest, cancellationToken);
        if (!userResult.Success)
        {
            logger.LogWarning("support_request_user_confirmation_email_failed for request {SupportRequestId}: {Error}", supportRequest.Id, userResult.ErrorMessage);
        }

        return new SupportRequestResult(true, "Recebemos sua solicitação. Nossa equipe vai responder por e-mail assim que possível.", supportRequest);
    }

    private static string? ValidateAttachment(SupportAttachmentInput attachment, long maxBytes)
    {
        if (attachment.Content.LongLength > maxBytes)
        {
            return $"Cada anexo deve ter no máximo {FormatBytes(maxBytes)}.";
        }

        var extension = Path.GetExtension(attachment.FileName);
        if (BlockedExtensions.Contains(extension)
            || !AllowedExtensions.Contains(extension)
            || !AllowedContentTypes.Contains(attachment.ContentType))
        {
            return "Formato de arquivo não permitido. Envie apenas PNG, JPG, JPEG ou PDF.";
        }

        return null;
    }

    private static string SanitizeFileName(string fileName)
    {
        var onlyName = Path.GetFileName(fileName);
        return string.IsNullOrWhiteSpace(onlyName) ? "anexo" : onlyName;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes % (1024 * 1024) == 0
            ? $"{bytes / (1024 * 1024)} MB"
            : $"{bytes} bytes";
    }
}

public sealed class PasswordResetService(
    LumaDbContext db,
    IEmailService emailService,
    IOptions<EmailOptions> emailOptions,
    ILogger<PasswordResetService> logger)
{
    public const string GenericForgotPasswordMessage = "Se existir uma conta vinculada a este e-mail, enviaremos instruções para redefinir sua senha.";
    private const int TokenBytes = 32;

    public async Task<PasswordResetRequestResult> RequestResetAsync(
        string email,
        string? requestIp = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return new PasswordResetRequestResult(true, GenericForgotPasswordMessage);
        }

        var emailHash = PrivacyRuntime.LookupHash(normalizedEmail, "account.email");
        var account = await db.AccountUsers.FirstOrDefaultAsync(user => user.EmailHash == emailHash, cancellationToken);
        if (account is null)
        {
            return new PasswordResetRequestResult(true, GenericForgotPasswordMessage);
        }

        var now = DateTimeOffset.UtcNow;
        var activeTokens = await db.PasswordResetTokens
            .Where(token => token.AccountUserId == account.Id && token.UsedAt == null && token.ExpiresAt >= now)
            .ToListAsync(cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.UsedAt = now;
        }

        var rawToken = CreateToken();
        var token = new PasswordResetToken
        {
            AccountUserId = account.Id,
            TokenHash = AccountSecurity.HashToken(rawToken),
            ExpiresAt = now.AddMinutes(Math.Max(5, emailOptions.Value.PasswordResetExpirationMinutes)),
            RequestIp = requestIp,
            UserAgent = userAgent,
            CreatedAt = now
        };
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);

        var resetUrl = ResendEmailService.BuildWebUrl(emailOptions.Value.WebBaseUrl, $"/reset-password?token={Uri.EscapeDataString(rawToken)}");
        var result = await emailService.SendPasswordResetEmailAsync(
            account.Email,
            account.FullName,
            resetUrl,
            Math.Max(5, emailOptions.Value.PasswordResetExpirationMinutes),
            cancellationToken);

        if (!result.Success)
        {
            logger.LogWarning("password_reset_email_failed for account {AccountId}: {Error}", account.Id, result.ErrorMessage);
        }

        return new PasswordResetRequestResult(true, GenericForgotPasswordMessage);
    }

    public async Task<PasswordResetRequestResult> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || newPassword.Length < 8)
        {
            return new PasswordResetRequestResult(false, "Link de recuperação inválido ou expirado.");
        }

        var tokenHash = AccountSecurity.HashToken(token);
        var resetToken = await db.PasswordResetTokens
            .Include(item => item.AccountUser)
            .Where(item => item.TokenHash == tokenHash)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (resetToken?.AccountUser is null || resetToken.UsedAt is not null || resetToken.ExpiresAt < now)
        {
            return new PasswordResetRequestResult(false, "Link de recuperação inválido ou expirado.");
        }

        resetToken.AccountUser.PasswordHash = AccountSecurity.HashPassword(newPassword);
        resetToken.AccountUser.UpdatedAt = now;
        resetToken.UsedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new PasswordResetRequestResult(true, "Senha redefinida com sucesso.");
    }

    private static string? NormalizeEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}
