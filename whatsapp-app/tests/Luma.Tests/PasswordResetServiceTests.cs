using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class PasswordResetServiceTests
{
    [Fact]
    public async Task RequestResetAsync_ReturnsGenericMessageAndSendsEmailWhenAccountExists()
    {
        await using var db = CreateDbContext();
        var account = CreateAccount("nay@example.com");
        db.AccountUsers.Add(account);
        await db.SaveChangesAsync();
        var email = new CapturingEmailService();
        var service = CreateService(db, email);

        var result = await service.RequestResetAsync("nay@example.com");

        Assert.Equal(PasswordResetService.GenericForgotPasswordMessage, result.Message);
        Assert.Single(db.PasswordResetTokens);
        Assert.DoesNotContain(email.ResetLinks.Single(), db.PasswordResetTokens.Single().TokenHash);
        Assert.Contains("/reset-password?token=", email.ResetLinks.Single());
        Assert.Equal("nay@example.com", email.ResetEmails.Single());
    }

    [Fact]
    public async Task RequestResetAsync_ReturnsSameMessageWhenEmailDoesNotExist()
    {
        await using var db = CreateDbContext();
        var email = new CapturingEmailService();
        var service = CreateService(db, email);

        var result = await service.RequestResetAsync("missing@example.com");

        Assert.Equal(PasswordResetService.GenericForgotPasswordMessage, result.Message);
        Assert.Empty(db.PasswordResetTokens);
        Assert.Empty(email.ResetEmails);
    }

    [Fact]
    public async Task ResetPasswordAsync_UpdatesPasswordAndUsesTokenOnce()
    {
        await using var db = CreateDbContext();
        var account = CreateAccount("nay@example.com");
        db.AccountUsers.Add(account);
        await db.SaveChangesAsync();
        var email = new CapturingEmailService();
        var service = CreateService(db, email);
        await service.RequestResetAsync("nay@example.com");
        var token = new Uri(email.ResetLinks.Single()).Query.Split("token=")[1];

        var result = await service.ResetPasswordAsync(token, "novaSenha123");
        var secondTry = await service.ResetPasswordAsync(token, "outraSenha123");

        Assert.True(result.Success);
        Assert.False(secondTry.Success);
        Assert.True(AccountSecurity.VerifyPassword("novaSenha123", account.PasswordHash));
        Assert.NotNull(db.PasswordResetTokens.Single().UsedAt);
    }

    private static PasswordResetService CreateService(LumaDbContext db, IEmailService email)
    {
        return new PasswordResetService(
            db,
            email,
            Options.Create(new EmailOptions
            {
                PasswordResetExpirationMinutes = 30,
                WebBaseUrl = "https://ia-luma.com.br"
            }),
            NullLogger<PasswordResetService>.Instance);
    }

    private static AccountUser CreateAccount(string email)
    {
        return new AccountUser
        {
            Email = email,
            Cpf = "45815168890",
            FullName = "Nayara Zago",
            PasswordHash = AccountSecurity.HashPassword("senhaAtual123"),
            PhoneNumber = "+5516992330309"
        };
    }

    private static LumaDbContext CreateDbContext()
    {
        PrivacyRuntime.Configure(new PrivacyOptions
        {
            EncryptionEnabled = false,
            LookupPepper = "tests"
        });

        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LumaDbContext(options);
    }

    private sealed class CapturingEmailService : IEmailService
    {
        public List<string> ResetEmails { get; } = [];
        public List<string> ResetLinks { get; } = [];

        public Task<EmailSendResult> SendWelcomeEmailAsync(string to, string? userName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EmailSendResult(true, "email_welcome", null));
        }

        public Task<EmailSendResult> SendSubscriptionCreatedEmailAsync(string to, string? userName, string planName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EmailSendResult(true, "email_subscription", null));
        }

        public Task<EmailSendResult> SendPasswordResetEmailAsync(string to, string? userName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
        {
            ResetEmails.Add(to);
            ResetLinks.Add(resetUrl);
            return Task.FromResult(new EmailSendResult(true, "email_reset", null));
        }
    }
}
