using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class AccountPhoneVerificationServiceTests
{
    [Fact]
    public async Task SendCurrentPhoneCodeAsync_CreatesCodeAndSendsWhatsAppMessage()
    {
        await using var db = CreateDbContext();
        var verify = new CapturingVerifyClient();
        var account = CreateAccount("+5516992330309");
        db.AccountUsers.Add(account);
        await db.SaveChangesAsync();

        var service = CreateService(db, verify);

        var result = await service.SendCurrentPhoneCodeAsync(account);

        Assert.True(result.Success);
        Assert.Single(db.AccountPhoneVerificationCodes);
        Assert.Equal("+5516992330309", verify.SentPhones.Single());
    }

    [Fact]
    public async Task ConfirmCurrentPhoneCodeAsync_MarksPhoneAsVerified()
    {
        await using var db = CreateDbContext();
        var account = CreateAccount("+5516992330309");
        db.AccountUsers.Add(account);
        await db.SaveChangesAsync();
        var verify = new CapturingVerifyClient();
        var service = CreateService(db, verify);
        await service.SendCurrentPhoneCodeAsync(account);
        verify.ApprovedCodes.Add("123456");

        var result = await service.ConfirmCurrentPhoneCodeAsync(account, "123456");

        Assert.True(result.Success);
        Assert.NotNull(account.PhoneVerifiedAt);
        Assert.NotNull(db.AccountPhoneVerificationCodes.Single().ConsumedAt);
    }

    [Fact]
    public async Task ConfirmPhoneChangeCodeAsync_UpdatesAccountAndSubscriptions()
    {
        await using var db = CreateDbContext();
        var account = CreateAccount("+5516992330309");
        db.AccountUsers.Add(account);
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            AccountUserId = account.Id,
            PhoneNumber = account.PhoneNumber,
            PlanCode = "essencial",
            BillingInterval = "monthly",
            Status = SubscriptionStatuses.Active,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
        var verify = new CapturingVerifyClient();
        var service = CreateService(db, verify);
        await service.SendPhoneChangeCodeAsync(account, "(16) 98830-7735");
        verify.ApprovedCodes.Add("654321");

        var result = await service.ConfirmPhoneChangeCodeAsync(account, "(16) 98830-7735", "654321");

        Assert.True(result.Success);
        Assert.Equal("+5516988307735", account.PhoneNumber);
        Assert.NotNull(account.PhoneVerifiedAt);
        Assert.Equal("+5516988307735", db.AccountSubscriptions.Single().PhoneNumber);
    }

    [Fact]
    public async Task ConfirmCurrentPhoneCodeAsync_RejectsWrongCode()
    {
        await using var db = CreateDbContext();
        var account = CreateAccount("+5516992330309");
        db.AccountUsers.Add(account);
        await db.SaveChangesAsync();
        var verify = new CapturingVerifyClient();
        var service = CreateService(db, verify);
        await service.SendCurrentPhoneCodeAsync(account);
        verify.ApprovedCodes.Add("123456");

        var result = await service.ConfirmCurrentPhoneCodeAsync(account, "000000");

        Assert.False(result.Success);
        Assert.Contains("Código inválido", result.Message);
    }

    private static AccountPhoneVerificationService CreateService(LumaDbContext db, ITwilioVerifyClient verify)
    {
        return new AccountPhoneVerificationService(
            db,
            verify,
            NullLogger<AccountPhoneVerificationService>.Instance);
    }

    private static AccountUser CreateAccount(string phone)
    {
        return new AccountUser
        {
            Email = $"{Guid.NewGuid():N}@luma.test",
            Cpf = "45815168890",
            FullName = "Nayara Zago",
            PasswordHash = AccountSecurity.HashPassword("12345678"),
            PhoneNumber = phone
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

    private sealed class CapturingVerifyClient : ITwilioVerifyClient
    {
        public List<string> SentPhones { get; } = [];
        public HashSet<string> ApprovedCodes { get; } = [];

        public Task<TwilioVerifySendResult> SendVerificationAsync(string to, CancellationToken cancellationToken = default)
        {
            SentPhones.Add(to);
            return Task.FromResult(new TwilioVerifySendResult(true, "VE_test", "pending", null));
        }

        public Task<TwilioVerifyCheckResult> CheckVerificationAsync(string to, string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApprovedCodes.Contains(code)
                ? new TwilioVerifyCheckResult(true, "approved", null)
                : new TwilioVerifyCheckResult(false, "pending", null));
        }
    }
}
