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
    public int PasswordResetExpirationMinutes { get; set; } = 30;
    public string WebBaseUrl { get; set; } = string.Empty;
}

public sealed class EmailTemplateOptions
{
    public string Welcome { get; set; } = string.Empty;
    public string SubscriptionCreated { get; set; } = string.Empty;
    public string PasswordReset { get; set; } = string.Empty;
}

public sealed record EmailSendResult(bool Success, string? ProviderMessageId, string? ErrorMessage);

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

    private async Task<EmailSendResult> SendTemplateAsync(
        string to,
        string templateId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken)
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
        request.Content = JsonContent.Create(new
        {
            from = _email.From,
            to = new[] { to },
            template = new
            {
                id = templateId,
                variables
            }
        });

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
}

public sealed record PasswordResetRequestResult(bool Success, string Message);

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
