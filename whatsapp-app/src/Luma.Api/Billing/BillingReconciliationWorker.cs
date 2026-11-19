using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Billing;

public sealed class BillingReconciliationWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<BillingReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Billing:ReconciliationEnabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
                var gateway = scope.ServiceProvider.GetRequiredService<ISubscriptionGateway>();
                if (!gateway.IsReady) continue;
                var requests = await db.AccountSubscriptions.AsNoTracking().Where(s => s.BillingProvider == gateway.Provider && s.TerminatedAt != null
                    && (s.TerminationConfirmedAt == null || (s.WithdrawalRequestedAt != null && s.WithdrawalCompletedAt == null)))
                    .OrderBy(s => s.UpdatedAt).Select(s => new { s.AccountUserId, s.Id, Withdrawal = s.WithdrawalRequestedAt != null }).Take(20).ToListAsync(stoppingToken);
                foreach (var item in requests)
                {
                    using var retry = scopes.CreateScope();
                    try { await retry.ServiceProvider.GetRequiredService<BillingService>().TerminateAsync(item.AccountUserId, item.Id, item.Withdrawal, stoppingToken); }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Billing request {SubscriptionId} awaits retry: {ErrorType}", item.Id, exception.GetType().Name); }
                }
                var deletions = await db.AccountUsers.AsNoTracking().Where(a => a.DeletionRequestedAt != null && a.DeletionCompletedAt == null)
                    .OrderBy(a => a.DeletionRequestedAt).Select(a => a.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var id in deletions)
                {
                    using var retry = scopes.CreateScope();
                    try { await retry.ServiceProvider.GetRequiredService<Luma.Api.Services.AccountDeletionService>().CompleteAsync(id, stoppingToken); }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Account deletion {AccountId} awaits retry: {ErrorType}", id, exception.GetType().Name); }
                }
                var before = DateTimeOffset.UtcNow.AddMinutes(-10);
                var subscriptions = await db.AccountSubscriptions.AsNoTracking().Where(s => s.BillingProvider == gateway.Provider && s.Status != "failed"
                    && s.PaymentAttemptedAt != null && s.UpdatedAt < before
                    && (s.Status == "pending" || s.CurrentPeriodEndsAt > DateTimeOffset.UtcNow.AddDays(-7)))
                    .OrderBy(s => s.UpdatedAt).Select(s => new { s.AccountUserId, s.Id }).Take(20).ToListAsync(stoppingToken);
                foreach (var subscription in subscriptions)
                {
                    using var itemScope = scopes.CreateScope();
                    try { await itemScope.ServiceProvider.GetRequiredService<BillingService>().RefreshAsync(subscription.AccountUserId, subscription.Id, stoppingToken); }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("Billing reconciliation deferred for subscription {SubscriptionId}: {ErrorType}", subscription.Id, exception.GetType().Name); }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Billing reconciliation deferred: {ErrorType}", exception.GetType().Name); }
        }
    }
}
