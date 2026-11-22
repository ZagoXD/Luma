using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Fiscal;

// Explicitly invoked by a future adapter/worker, never scheduled while fiscal emission is disabled.
public sealed class FiscalProcessor(LumaDbContext db, IFiscalGateway gateway)
{
    public async Task ProcessAsync(Guid id, bool accountingPolicyApproved, CancellationToken ct)
    {
        if (!accountingPolicyApproved || gateway is DisabledFiscalGateway) return;
        FiscalDocument row;
        bool reconcile;
        await using (var tx = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            if (tx is not null) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 27))", ct);
            row = await db.FiscalDocuments.SingleAsync(x => x.Id == id, ct);
            if (row.Status is "authorized" or "review_required" or "rejected" || row.NextAttemptAt > DateTimeOffset.UtcNow) return;
            reconcile = row.Status == "awaiting_reconciliation";
            row.Status = "awaiting_reconciliation";
            row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
            row.Attempts++;
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        var key = "luma-fiscal-" + row.Id.ToString("N");
        FiscalGatewayResult result;
        try
        {
            // Reconcile uncertain outcomes instead of blindly issuing the same invoice twice.
            if (reconcile) result = await gateway.QueryAsync(key, ct);
            else
            {
                result = await gateway.IssueAsync(new(key, row.Amount, row.Currency, row.PaidAt), ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            row.Status = "awaiting_reconciliation";
            row.LastError = "Resultado incerto: consultar o provedor antes de tentar novamente.";
            row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        await db.Entry(row).ReloadAsync(ct);
        if (row.Status == "review_required") return;
        if (result.Status == "authorized" && (string.IsNullOrWhiteSpace(result.Number) || string.IsNullOrWhiteSpace(result.AccessKey)))
            result = new("review_required", Error: "Resposta fiscal incompleta.");
        row.Status = result.Status switch { "authorized" => "authorized", "rejected" => "rejected", "not_configured" => "awaiting_configuration", "review_required" => "review_required", _ => "awaiting_reconciliation" };
        row.ProviderId = result.ProviderId;
        row.Number = result.Number;
        row.AccessKey = result.AccessKey;
        row.LastError = result.Error is null ? null : "O provedor fiscal requer revisão manual.";
        row.NextAttemptAt = row.Status == "awaiting_reconciliation" ? DateTimeOffset.UtcNow.AddMinutes(5) : null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
