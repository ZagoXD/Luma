using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public static class AccountPhoneVerificationPurposes
{
    public const string Registration = "registration";
    public const string PhoneChange = "phone_change";
}

public sealed record AccountPhoneVerificationResult(bool Success, string Message);

public sealed class AccountPhoneVerificationService(
    LumaDbContext db,
    ITwilioVerifyClient verifyClient,
    ILogger<AccountPhoneVerificationService> logger)
{
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 5;

    public async Task<AccountPhoneVerificationResult> SendCurrentPhoneCodeAsync(AccountUser account, CancellationToken cancellationToken = default)
    {
        return await CreateAndSendCodeAsync(account, account.PhoneNumber, AccountPhoneVerificationPurposes.Registration, cancellationToken);
    }

    public async Task<AccountPhoneVerificationResult> SendPhoneChangeCodeAsync(AccountUser account, string requestedPhoneNumber, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = AccountInputNormalizer.NormalizeBrazilPhone(requestedPhoneNumber);
        if (normalizedPhone is null)
        {
            return new AccountPhoneVerificationResult(false, "Informe um celular válido com DDD.");
        }

        var phoneHash = PrivacyRuntime.LookupHash(normalizedPhone, "account.phone");
        var alreadyUsed = await db.AccountUsers.AnyAsync(user => user.Id != account.Id && user.PhoneHash == phoneHash, cancellationToken);
        if (alreadyUsed)
        {
            return new AccountPhoneVerificationResult(false, "Já existe uma conta usando esse celular.");
        }

        return await CreateAndSendCodeAsync(account, normalizedPhone, AccountPhoneVerificationPurposes.PhoneChange, cancellationToken);
    }

    public async Task<AccountPhoneVerificationResult> ConfirmCurrentPhoneCodeAsync(AccountUser account, string code, CancellationToken cancellationToken = default)
    {
        var result = await ConfirmCodeAsync(account, account.PhoneNumber, AccountPhoneVerificationPurposes.Registration, code, cancellationToken);
        if (!result.Success)
        {
            return result;
        }

        account.PhoneVerifiedAt = DateTimeOffset.UtcNow;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new AccountPhoneVerificationResult(true, "Celular confirmado com sucesso.");
    }

    public async Task<AccountPhoneVerificationResult> ConfirmPhoneChangeCodeAsync(AccountUser account, string requestedPhoneNumber, string code, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = AccountInputNormalizer.NormalizeBrazilPhone(requestedPhoneNumber);
        if (normalizedPhone is null)
        {
            return new AccountPhoneVerificationResult(false, "Informe um celular válido com DDD.");
        }

        var phoneHash = PrivacyRuntime.LookupHash(normalizedPhone, "account.phone");
        var alreadyUsed = await db.AccountUsers.AnyAsync(user => user.Id != account.Id && user.PhoneHash == phoneHash, cancellationToken);
        if (alreadyUsed)
        {
            return new AccountPhoneVerificationResult(false, "Já existe uma conta usando esse celular.");
        }

        var result = await ConfirmCodeAsync(account, normalizedPhone, AccountPhoneVerificationPurposes.PhoneChange, code, cancellationToken);
        if (!result.Success)
        {
            return result;
        }

        var previousAccountPhone = account.PhoneNumber;
        account.PhoneNumber = normalizedPhone;
        account.PhoneVerifiedAt = DateTimeOffset.UtcNow;
        account.UpdatedAt = DateTimeOffset.UtcNow;

        var subscriptions = await db.AccountSubscriptions
            .Where(subscription => subscription.AccountUserId == account.Id)
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscription.PhoneNumber = normalizedPhone;
            subscription.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await MigrateLumaUserPhoneIfPossibleAsync(previousAccountPhone, normalizedPhone, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new AccountPhoneVerificationResult(true, "Celular atualizado e confirmado com sucesso.");
    }

    private async Task<AccountPhoneVerificationResult> CreateAndSendCodeAsync(AccountUser account, string phoneNumber, string purpose, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var phoneHash = PrivacyRuntime.LookupHash(phoneNumber, "account.phone");

        var activeCodes = await db.AccountPhoneVerificationCodes
            .Where(item => item.AccountUserId == account.Id
                && item.PhoneHash == phoneHash
                && item.Purpose == purpose
                && item.ConsumedAt == null
                && item.ExpiresAt >= now)
            .ToListAsync(cancellationToken);

        foreach (var activeCode in activeCodes)
        {
            activeCode.ConsumedAt = now;
        }

        db.AccountPhoneVerificationCodes.Add(new AccountPhoneVerificationCode
        {
            AccountUserId = account.Id,
            PhoneNumber = phoneNumber,
            Purpose = purpose,
            CodeHash = "twilio_verify",
            ExpiresAt = now.Add(CodeTtl),
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);

        var sendResult = await verifyClient.SendVerificationAsync(phoneNumber, cancellationToken);
        if (!sendResult.Success)
        {
            logger.LogWarning("Could not send account phone verification code to {Phone}. Error: {Error}", PhoneNumber.Mask(phoneNumber), sendResult.ErrorMessage);
            return new AccountPhoneVerificationResult(false, "Não consegui enviar o código pelo WhatsApp agora. Tente novamente em instantes.");
        }

        return new AccountPhoneVerificationResult(true, "Código enviado pelo WhatsApp.");
    }

    private async Task<AccountPhoneVerificationResult> ConfirmCodeAsync(AccountUser account, string phoneNumber, string purpose, string code, CancellationToken cancellationToken)
    {
        var normalizedCode = AccountInputNormalizer.OnlyDigits(code);
        if (normalizedCode.Length != 6)
        {
            return new AccountPhoneVerificationResult(false, "Informe o código de 6 dígitos enviado pelo WhatsApp.");
        }

        var now = DateTimeOffset.UtcNow;
        var phoneHash = PrivacyRuntime.LookupHash(phoneNumber, "account.phone");
        var verificationCode = await db.AccountPhoneVerificationCodes
            .Where(item => item.AccountUserId == account.Id
                && item.PhoneHash == phoneHash
                && item.Purpose == purpose
                && item.ConsumedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode is null || verificationCode.ExpiresAt < now)
        {
            return new AccountPhoneVerificationResult(false, "Código expirado ou não encontrado. Solicite um novo código.");
        }

        if (verificationCode.Attempts >= MaxAttempts)
        {
            return new AccountPhoneVerificationResult(false, "Muitas tentativas incorretas. Solicite um novo código.");
        }

        var check = await verifyClient.CheckVerificationAsync(phoneNumber, normalizedCode, cancellationToken);
        if (!check.Approved)
        {
            verificationCode.Attempts += 1;
            await db.SaveChangesAsync(cancellationToken);
            return new AccountPhoneVerificationResult(false, "Código inválido. Confira a mensagem recebida no WhatsApp e tente novamente.");
        }

        verificationCode.ConsumedAt = now;
        return new AccountPhoneVerificationResult(true, "Código confirmado.");
    }

    private async Task MigrateLumaUserPhoneIfPossibleAsync(string previousPhone, string nextPhone, CancellationToken cancellationToken)
    {
        var previousHash = PrivacyRuntime.LookupHash(previousPhone, "user.phone");
        var nextHash = PrivacyRuntime.LookupHash(nextPhone, "user.phone");
        var previousUser = await db.Users.FirstOrDefaultAsync(user => user.PhoneHash == previousHash, cancellationToken);
        if (previousUser is null)
        {
            return;
        }

        var nextUserExists = await db.Users.AnyAsync(user => user.Id != previousUser.Id && user.PhoneHash == nextHash, cancellationToken);
        if (nextUserExists)
        {
            return;
        }

        previousUser.PhoneNumber = nextPhone;
        previousUser.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
