using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Luma.Api.Billing;

public sealed class BillingService(LumaDbContext db, ISubscriptionGateway gateway, IConfiguration configuration,
    IEmailService emails, ILogger<BillingService> logger)
{
    public async Task<AccountSubscription> PrepareAsync(AccountUser user, string planCode, string interval, CancellationToken ct, Guid? replaces = null)
    {
        BillingCatalog.Get(planCode, interval);
        if (user.PhoneVerifiedAt is null) throw new BillingException("Confirme seu celular antes de contratar um plano.");
        await using var transaction = await LockAsync(user.Id, ct);
        var pending = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.AccountUserId == user.Id
            && s.BillingProvider == gateway.Provider && s.Status == SubscriptionStatuses.Pending
            && s.PlanCode == planCode && s.BillingInterval == interval && s.ReplacesSubscriptionId == replaces, ct);
        var current = await db.AccountSubscriptions.Where(s => s.AccountUserId == user.Id && s.CurrentPeriodEndsAt > DateTimeOffset.UtcNow
            && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Canceled)).OrderByDescending(s => s.CurrentPeriodEndsAt).FirstOrDefaultAsync(ct);
        if (current is not null && current.Id != replaces)
            throw new BillingException("Você já possui acesso. Gerencie sua assinatura no perfil para evitar cobranças duplicadas.");
        if (replaces is not null && (current is null || current.Id != replaces || (current.ReplacedBySubscriptionId is not null && current.ReplacedBySubscriptionId != pending?.Id)
            || current.BillingProvider != gateway.Provider || current.PaymentMethod != "card" || current.CurrentPeriodEndsAt <= DateTimeOffset.UtcNow.AddHours(2)))
            throw new BillingException("Não foi possível agendar essa troca. Consulte seu perfil ou entre em contato com o suporte.");
        if (pending is null)
        {
            pending = new() { AccountUserId = user.Id, PhoneNumber = user.PhoneNumber, PlanCode = planCode, BillingInterval = interval,
                BillingProvider = gateway.Provider, Status = SubscriptionStatuses.Pending, CurrentPeriodEndsAt = DateTimeOffset.UtcNow };
            pending.ReplacesSubscriptionId = replaces;
            if (current is not null) pending.StartsAt = current.CurrentPeriodEndsAt;
            db.AccountSubscriptions.Add(pending);
            await db.SaveChangesAsync(ct);
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return pending;
    }

    public async Task<AccountSubscription> SubmitAsync(AccountUser user, Guid id, string? token, string method, string? payerEmail, CancellationToken ct)
    {
        if (method is not ("card" or "pix")) throw new BillingException("Forma de pagamento inválida.");
        if (method == "pix" && !gateway.SupportsPix) throw new BillingException("Pix ainda não está disponível neste ambiente. Você pode usar cartão.");
        if (method == "card" && (string.IsNullOrWhiteSpace(token) || token.Length > 256)) throw new BillingException("Token do cartão inválido.");
        await using var transaction = await LockAsync(user.Id, ct);
        var local = await OwnedAsync(user.Id, id, ct);
        if (local.Status != SubscriptionStatuses.Pending) return local;
        if (local.ReplacesSubscriptionId is not null && method != "card")
            throw new BillingException("A troca da recorrência requer uma nova autorização de cartão. Pix pode ser contratado ao fim do período pago.");
        if (await db.AccountSubscriptions.AnyAsync(s => s.AccountUserId == user.Id && s.Id != id
            && ((s.Id != local.ReplacesSubscriptionId && s.CurrentPeriodEndsAt > DateTimeOffset.UtcNow && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Canceled))
                || (s.Status == SubscriptionStatuses.Pending && s.PaymentAttemptedAt != null)), ct))
            throw new BillingException("Você já possui acesso. Consulte seu perfil antes de fazer uma nova compra.");
        if (local.PaymentAttemptedAt is not null && local.PaymentMethod != method)
            throw new BillingException("Já existe um pagamento em processamento para este pedido. Aguarde a confirmação antes de trocar a forma de pagamento.");
        if (local.ProviderSubscriptionId is null)
        {
            var returnUrl = (configuration["Luma:WebBaseUrl"] ?? "http://localhost:3000").TrimEnd('/') + "/perfil";
            var email = string.IsNullOrWhiteSpace(payerEmail) ? user.Email : payerEmail.Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email || email.Length > 254)
                throw new BillingException("E-mail do pagador inválido.");
            var order = new SubscriptionOrder(local.Id, email, BillingCatalog.Get(local.PlanCode, local.BillingInterval), returnUrl,
                local.ReplacesSubscriptionId is not null ? local.StartsAt : null);
            local.PaymentAttemptedAt ??= DateTimeOffset.UtcNow;
            local.PaymentMethod = method;
            await db.SaveChangesAsync(ct);
            ProviderSubscription remote;
            try
            {
                remote = method == "pix" ? await gateway.CreatePixAsync(order, user.Cpf, user.FullName, ct)
                    : await gateway.CreateAsync(order, token!, ct);
            }
            catch (Exception exception) when (exception is BillingException or HttpRequestException or TaskCanceledException)
            {
                if (exception is BillingException { Uncertain: false }) local.PaymentAttemptedAt = null;
                await db.SaveChangesAsync(CancellationToken.None);
                if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
                throw;
            }
            if (!BillingPolicy.Matches(local, remote)) throw new BillingException("Não foi possível validar os dados da cobrança.");
            local.PaymentMethod = method;
            local.ProviderSubscriptionId = remote.Id;
            await db.SaveChangesAsync(ct);
        }
        try { await SyncAsync(local, ct); }
        catch (Exception exception) when (exception is BillingException or HttpRequestException or TaskCanceledException)
        {
            // A remote authorization may already exist. Persist its ID even if confirmation is temporarily unavailable.
            if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
            throw;
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return local;
    }

    public async Task<AccountSubscription> RefreshAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var transaction = await LockAsync(userId, ct);
        var local = await OwnedAsync(userId, id, ct);
        if (local.ProviderSubscriptionId is null && local.PaymentAttemptedAt is not null)
        {
            var recovered = await gateway.FindAsync(local.Id, local.PaymentMethod, ct);
            if (recovered is not null)
            {
                if (!BillingPolicy.Matches(local, recovered)) throw new BillingException("Cobrança incompatível com este pedido.");
                local.ProviderSubscriptionId = recovered.Id;
                await db.SaveChangesAsync(ct);
            }
        }
        if (local.ProviderSubscriptionId is not null && local.BillingProvider == gateway.Provider) await SyncAsync(local, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return local;
    }

    public async Task<AccountSubscription> SetRenewalAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        await using var transaction = await LockAsync(userId, ct);
        var local = await CurrentAsync(userId, ct);
        if (local.ReplacedBySubscriptionId is not null) throw new BillingException("Existe uma troca agendada. Cancele a troca antes de alterar a renovação atual.");
        if (local.PaymentMethod != "card") throw new BillingException("O Pix não possui renovação automática. Uma nova compra pode ser feita ao fim do acesso.");
        await gateway.SetRenewalAsync(local.ProviderSubscriptionId!, enabled, ct);
        local.Status = enabled ? SubscriptionStatuses.Active : SubscriptionStatuses.Canceled;
        local.CanceledAt = enabled ? null : DateTimeOffset.UtcNow;
        local.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return local;
    }

    public async Task<AccountSubscription> ChangePlanAsync(Guid userId, string code, string interval, CancellationToken ct)
    {
        var plan = BillingCatalog.Get(code, interval);
        await using var transaction = await LockAsync(userId, ct);
        var local = await CurrentAsync(userId, ct);
        if (interval != local.BillingInterval) throw new BillingException("A troca entre mensal e anual exige uma nova autorização no checkout.");
        if (local.PaymentMethod != "card" || local.Status != SubscriptionStatuses.Active)
            throw new BillingException("Retome a renovação antes de alterar o plano. No Pix, escolha o novo plano ao renovar.");
        // Keep the paid entitlement unchanged; the new tier is granted only when its renewal is paid.
        var previousPlan = local.PendingPlanCode;
        var previousInterval = local.PendingBillingInterval;
        local.PendingPlanCode = code;
        local.PendingBillingInterval = interval;
        await db.SaveChangesAsync(ct);
        try { await gateway.ChangePlanAsync(local.ProviderSubscriptionId!, plan, local.CurrentPeriodEndsAt, ct); }
        catch (Exception exception) when (exception is BillingException or HttpRequestException or TaskCanceledException)
        {
            if (exception is BillingException { Uncertain: false })
            {
                local.PendingPlanCode = previousPlan;
                local.PendingBillingInterval = previousInterval;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
            throw;
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return local;
    }

    public async Task ChangeCardAsync(Guid userId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) throw new BillingException("Token do cartão inválido.");
        await using var transaction = await LockAsync(userId, ct);
        var local = await CurrentAsync(userId, ct);
        if (local.PaymentMethod != "card") throw new BillingException("Este pagamento não possui cartão de renovação.");
        await gateway.ChangeCardAsync(local.ProviderSubscriptionId!, token, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task CancelScheduledAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var transaction = await LockAsync(userId, ct);
        var scheduled = await OwnedAsync(userId, id, ct);
        if (scheduled.ReplacesSubscriptionId is not Guid previousId || scheduled.StartsAt <= DateTimeOffset.UtcNow
            || scheduled.ProviderSubscriptionId is null || scheduled.Status != "pending")
            throw new BillingException("Nenhuma troca futura disponível para cancelar.");
        await gateway.SetRenewalAsync(scheduled.ProviderSubscriptionId, false, ct);
        scheduled.Status = "failed";
        var previous = await db.AccountSubscriptions.SingleAsync(s => s.Id == previousId && s.AccountUserId == userId, ct);
        previous.ReplacedBySubscriptionId = null;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    public async Task ReceiveAsync(string topic, string resourceId, CancellationToken ct)
    {
        var remoteId = await gateway.ResolveSubscriptionAsync(topic, resourceId, ct);
        if (string.IsNullOrWhiteSpace(remoteId)) return;
        var remote = await gateway.GetAsync(remoteId, ct);
        if (!Guid.TryParseExact(remote.Reference, "N", out var localId)) return;
        var owner = await db.AccountSubscriptions.AsNoTracking().Where(s => s.Id == localId && s.BillingProvider == gateway.Provider)
            .Select(s => (Guid?)s.AccountUserId).FirstOrDefaultAsync(ct);
        if (owner is null) return;
        await using var transaction = await LockAsync(owner.Value, ct);
        var local = await OwnedAsync(owner.Value, localId, ct);
        if (local.ProviderSubscriptionId is not null && local.ProviderSubscriptionId != remoteId)
            throw new BillingException("Cobrança incompatível com o pedido.");
        local.ProviderSubscriptionId = remoteId;
        await SyncAsync(local, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private async Task SyncAsync(AccountSubscription local, CancellationToken ct)
    {
        var remote = await gateway.GetAsync(local.ProviderSubscriptionId!, ct);
        if (local.ReplacesSubscriptionId is Guid previousId && remote.Status == "authorized")
        {
            var previous = await db.AccountSubscriptions.SingleAsync(s => s.Id == previousId && s.AccountUserId == local.AccountUserId, ct);
            if (previous.ReplacedBySubscriptionId != local.Id)
            {
                if (previous.ProviderSubscriptionId is null) throw new BillingException("Assinatura anterior inválida.");
                await gateway.SetRenewalAsync(previous.ProviderSubscriptionId, false, ct);
                previous.ReplacedBySubscriptionId = local.Id;
                previous.Status = SubscriptionStatuses.Canceled;
                previous.CanceledAt = DateTimeOffset.UtcNow;
                local.ReplacementConfirmedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        var effective = local;
        if (local.PendingPlanCode is not null && local.PendingBillingInterval is not null)
            effective = new() { Id = local.Id, PlanCode = local.PendingPlanCode, BillingInterval = local.PendingBillingInterval };
        if (!BillingPolicy.Matches(effective, remote)) throw new BillingException("Plano, valor ou período da cobrança incompatível.");
        var wasPending = local.Status == SubscriptionStatuses.Pending;
        foreach (var payment in await gateway.GetPaymentsAsync(remote.Id, ct))
        {
            var key = gateway.Provider + ":" + payment.Id;
            var saved = await db.BillingPayments.SingleOrDefaultAsync(p => p.Id == key, ct);
            if (saved is not null)
            {
                if (saved.AccountSubscriptionId != local.Id) throw new BillingException("Pagamento já vinculado a outro pedido.");
                saved.Status = payment.Status;
                continue;
            }
            if (!BillingPolicy.IsPaid(effective, remote, payment) || payment.ApprovedAt > DateTimeOffset.UtcNow.AddMinutes(5)
                || payment.ApprovedAt < local.StartsAt.AddMinutes(-5)) continue;
            var plan = BillingCatalog.Get(effective.PlanCode, effective.BillingInterval);
            db.BillingPayments.Add(new() { Id = key, AccountSubscriptionId = local.Id, Status = payment.Status,
                Amount = payment.Amount, Currency = payment.Currency, ApprovedAt = payment.ApprovedAt, PeriodEndsAt = plan.PeriodEnd(payment.ApprovedAt) });
            local.PlanCode = effective.PlanCode;
            local.BillingInterval = effective.BillingInterval;
            local.PendingPlanCode = null;
            local.PendingBillingInterval = null;
        }
        await db.SaveChangesAsync(ct);
        var end = await db.BillingPayments.Where(p => p.AccountSubscriptionId == local.Id && p.Status == "approved")
            .MaxAsync(p => (DateTimeOffset?)p.PeriodEndsAt, ct);
        local.CurrentPeriodEndsAt = end ?? local.StartsAt;
        if (end > DateTimeOffset.UtcNow)
        {
            local.Status = remote.Status is "paused" or "cancelled" or "canceled" || local.PaymentMethod == "pix"
                ? SubscriptionStatuses.Canceled : SubscriptionStatuses.Active;
            local.CanceledAt = local.Status == SubscriptionStatuses.Canceled ? local.CanceledAt ?? DateTimeOffset.UtcNow : null;
        }
        else local.Status = remote.Status is "cancelled" or "canceled" or "rejected" || local.Status == "failed" ? "failed" : SubscriptionStatuses.Pending;
        local.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (wasPending && end > DateTimeOffset.UtcNow)
        {
            var account = await db.AccountUsers.SingleAsync(u => u.Id == local.AccountUserId, ct);
            var result = await emails.SendSubscriptionCreatedEmailAsync(account.Email, account.FullName, local.PlanCode == "essencial" ? "Essencial" : "Básico");
            if (!result.Success) logger.LogWarning("Subscription email failed for account {AccountId}", account.Id);
        }
    }

    private async Task<AccountSubscription> OwnedAsync(Guid userId, Guid id, CancellationToken ct)
        => await db.AccountSubscriptions.SingleOrDefaultAsync(s => s.AccountUserId == userId && s.Id == id && s.BillingProvider == gateway.Provider, ct)
            ?? throw new BillingException("Pedido não encontrado.");
    private async Task<AccountSubscription> CurrentAsync(Guid userId, CancellationToken ct)
    {
        var current = await db.AccountSubscriptions.Where(s => s.AccountUserId == userId && s.CurrentPeriodEndsAt > DateTimeOffset.UtcNow
            && (s.Status == SubscriptionStatuses.Active || s.Status == SubscriptionStatuses.Canceled)).OrderByDescending(s => s.CurrentPeriodEndsAt).FirstOrDefaultAsync(ct)
            ?? throw new BillingException("Nenhuma assinatura paga disponível para esta ação.");
        if (current.BillingProvider != gateway.Provider || string.IsNullOrWhiteSpace(current.ProviderSubscriptionId))
            throw new BillingException("Este plano foi contratado no provedor anterior. Entre em contato com o suporte para migrar sem cobrança duplicada.");
        return current;
    }

    private async Task<IDbContextTransaction?> LockAsync(Guid accountId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))", ct);
        return transaction;
    }
}
