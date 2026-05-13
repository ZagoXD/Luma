using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class SupportRequestServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsRequestAndSendsAdminAndUserEmails()
    {
        await using var db = CreateDbContext();
        var account = CreateAccount();
        db.AccountUsers.Add(account);
        await db.SaveChangesAsync();
        var email = new CapturingEmailService();
        var service = CreateService(db, email);

        var result = await service.CreateAsync(account, "Problema no calendário", "Meu calendário não abriu.", [
            new SupportAttachmentInput("calendario.png", "image/png", [1, 2, 3])
        ]);

        Assert.True(result.Success);
        var request = Assert.Single(db.SupportRequests);
        Assert.Equal(account.Id, request.UserId);
        Assert.Equal("Problema no calendário", request.Subject);
        Assert.Equal(1, request.AttachmentCount);
        var metadata = Assert.Single(db.SupportRequestAttachmentMetadata);
        Assert.Equal("calendario.png", metadata.FileName);
        Assert.Single(email.AdminRequests);
        Assert.Single(email.UserConfirmations);
        Assert.Single(email.AdminAttachments.Single());
        Assert.Empty(email.UserAttachments.Single());
    }

    [Theory]
    [InlineData("", "descrição", "Informe o assunto")]
    [InlineData("Assunto", "", "Informe a descrição")]
    public async Task CreateAsync_RejectsRequiredFields(string subject, string description, string expectedMessage)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new CapturingEmailService());

        var result = await service.CreateAsync(CreateAccount(), subject, description, []);

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.Message);
        Assert.Empty(db.SupportRequests);
    }

    [Fact]
    public async Task CreateAsync_RejectsTooManyAttachments()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new CapturingEmailService());

        var result = await service.CreateAsync(CreateAccount(), "Ajuda", "Descrição", [
            new SupportAttachmentInput("a.png", "image/png", [1]),
            new SupportAttachmentInput("b.png", "image/png", [1]),
            new SupportAttachmentInput("c.png", "image/png", [1]),
            new SupportAttachmentInput("d.png", "image/png", [1])
        ]);

        Assert.False(result.Success);
        Assert.Contains("no máximo 3 anexos", result.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidAttachmentType()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new CapturingEmailService());

        var result = await service.CreateAsync(CreateAccount(), "Ajuda", "Descrição", [
            new SupportAttachmentInput("script.js", "application/javascript", [1])
        ]);

        Assert.False(result.Success);
        Assert.Contains("Formato de arquivo não permitido", result.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsOversizedAttachment()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new CapturingEmailService());

        var result = await service.CreateAsync(CreateAccount(), "Ajuda", "Descrição", [
            new SupportAttachmentInput("arquivo.pdf", "application/pdf", new byte[11])
        ]);

        Assert.False(result.Success);
        Assert.Contains("no máximo 10 bytes", result.Message);
    }

    private static SupportRequestService CreateService(LumaDbContext db, IEmailService email)
    {
        return new SupportRequestService(
            db,
            email,
            Options.Create(new EmailOptions
            {
                MaxSupportAttachments = 3,
                MaxSupportAttachmentBytes = 10,
                SupportDailyLimit = 5
            }),
            NullLogger<SupportRequestService>.Instance);
    }

    private static AccountUser CreateAccount()
    {
        return new AccountUser
        {
            Email = "nay@example.com",
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
        public List<SupportRequest> AdminRequests { get; } = [];
        public List<SupportRequest> UserConfirmations { get; } = [];
        public List<IReadOnlyList<EmailAttachment>> AdminAttachments { get; } = [];
        public List<IReadOnlyList<EmailAttachment>> UserAttachments { get; } = [];

        public Task<EmailSendResult> SendWelcomeEmailAsync(string to, string? userName, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailSendResult(true, "welcome", null));

        public Task<EmailSendResult> SendSubscriptionCreatedEmailAsync(string to, string? userName, string planName, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailSendResult(true, "subscription", null));

        public Task<EmailSendResult> SendPasswordResetEmailAsync(string to, string? userName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailSendResult(true, "reset", null));

        public Task<EmailSendResult> SendSupportRequestToAdminAsync(SupportRequest request, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken = default)
        {
            AdminRequests.Add(request);
            AdminAttachments.Add(attachments);
            return Task.FromResult(new EmailSendResult(true, "admin", null));
        }

        public Task<EmailSendResult> SendSupportRequestConfirmationToUserAsync(SupportRequest request, CancellationToken cancellationToken = default)
        {
            UserConfirmations.Add(request);
            UserAttachments.Add([]);
            return Task.FromResult(new EmailSendResult(true, "user", null));
        }
    }
}
