using Luma.Api.Billing;
using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed class AccountDeletionService(LumaDbContext db, BillingService billing, IUserImageDeletion? images = null)
{
    public async Task RequestAsync(Guid accountId, string password, CancellationToken ct)
    {
        var account = await db.AccountUsers.SingleAsync(a => a.Id == accountId, ct);
        if (string.IsNullOrWhiteSpace(password) || password.Length > 1024 || !AccountSecurity.VerifyPassword(password, account.PasswordHash))
            throw new BillingException("Confirme sua senha atual para excluir a conta.");
        await using (var transaction = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            if (transaction is not null) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))", ct);
            account.DeletionRequestedAt ??= DateTimeOffset.UtcNow;
            // Disabling access is durable even when the provider is temporarily unavailable.
            var subscriptions = await db.AccountSubscriptions.Where(s => s.AccountUserId == accountId).ToListAsync(ct);
            foreach (var subscription in subscriptions) { subscription.Status = "terminated"; subscription.TerminatedAt ??= DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        await CompleteAsync(accountId, ct);
    }

    public async Task CompleteAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.AccountUsers.SingleOrDefaultAsync(a => a.Id == accountId && a.DeletionRequestedAt != null && a.DeletionCompletedAt == null, ct);
        if (account is null) return;
        var subscriptions = await db.AccountSubscriptions.Where(s => s.AccountUserId == accountId).ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            if (subscription.BillingProvider == "mercadopago") await billing.TerminateAsync(accountId, subscription.Id, false, ct);
            else if (subscription.ProviderSubscriptionId is not null)
                throw new BillingException("Existe uma assinatura de outro provedor. A exclusão aguarda a interrupção da cobrança pelo suporte.");
        }
        var phoneHash = PrivacyRuntime.LookupHash(account.PhoneNumber, "user.phone");
        var users = await db.Users.Where(u => u.PhoneHash == phoneHash).ToListAsync(ct);
        if (images is not null) foreach (var user in users) await images.DeleteAsync(user.Id, ct);
        var ids = users.Select(u => u.Id).ToArray();
        var blockedHash = PrivacyRuntime.LookupHash(account.PhoneNumber, "blocked_conversation.from");
        var whatsappHash = PrivacyRuntime.LookupHash("whatsapp:" + account.PhoneNumber, "blocked_conversation.from");
        db.RemoveRange(await db.BlockedConversations.Where(x => x.FromHash == blockedHash || x.FromHash == whatsappHash).ToListAsync(ct));
        db.RemoveRange(await db.AssistantTurns.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.Messages.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.PendingIntents.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.CycleEvents.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.Cycles.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.Pregnancies.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.Consents.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.UserPreferences.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.NotificationPreferences.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(await db.NotificationDeliveries.Where(x => ids.Contains(x.UserId)).ToListAsync(ct));
        db.RemoveRange(users);
        db.RemoveRange(await db.AccountSessions.Where(x => x.AccountUserId == accountId).ToListAsync(ct));
        db.RemoveRange(await db.AccountPhoneVerificationCodes.Where(x => x.AccountUserId == accountId).ToListAsync(ct));
        db.RemoveRange(await db.PasswordResetTokens.Where(x => x.AccountUserId == accountId).ToListAsync(ct));
        db.RemoveRange(await db.EmailLogs.Where(x => x.To == account.Email).ToListAsync(ct));
        var support = await db.SupportRequests.Include(x => x.Attachments).Where(x => x.UserId == accountId).ToListAsync(ct);
        db.RemoveRange(support.SelectMany(x => x.Attachments));
        db.RemoveRange(support);
        // Keep only an unlinkable financial tombstone, not health or contact details. Webhooks cannot recreate access.
        var marker = "deleted-" + Guid.NewGuid().ToString("N");
        account.Email = marker + "@deleted.invalid";
        account.Cpf = marker;
        account.PhoneNumber = marker;
        account.FullName = "Conta excluída";
        account.PasswordHash = "";
        account.BillingCustomerId = null;
        account.TermsVersion = null;
        account.PrivacyVersion = null;
        account.TermsAcceptedAt = null;
        account.HealthConsentAcceptedAt = null;
        account.DeletionCompletedAt = DateTimeOffset.UtcNow;
        foreach (var subscription in subscriptions) subscription.PhoneNumber = marker;
        await db.SaveChangesAsync(ct);
    }
}
