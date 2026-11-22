using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Fiscal;

public sealed class FiscalDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PaymentId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public DateTimeOffset PaidAt { get; set; }
    public string Status { get; set; } = "awaiting_configuration";
    public string? Number { get; set; }
    public string? AccessKey { get; set; }
    public string? ProviderId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record FiscalIssueRequest(string IdempotencyKey, decimal Amount, string Currency, DateTimeOffset PaidAt);
public sealed record FiscalGatewayResult(string Status, string? ProviderId = null, string? Number = null, string? AccessKey = null, string? Error = null);
public interface IFiscalGateway
{
    Task<FiscalGatewayResult> IssueAsync(FiscalIssueRequest request, CancellationToken ct);
    Task<FiscalGatewayResult> QueryAsync(string idempotencyKey, CancellationToken ct);
    Task<FiscalGatewayResult> RequestCancellationAsync(string providerId, string reason, CancellationToken ct);
}
public sealed class DisabledFiscalGateway : IFiscalGateway
{
    public Task<FiscalGatewayResult> IssueAsync(FiscalIssueRequest request, CancellationToken ct) => Task.FromResult(new FiscalGatewayResult("not_configured"));
    public Task<FiscalGatewayResult> QueryAsync(string idempotencyKey, CancellationToken ct) => Task.FromResult(new FiscalGatewayResult("not_configured"));
    public Task<FiscalGatewayResult> RequestCancellationAsync(string providerId, string reason, CancellationToken ct) => Task.FromResult(new FiscalGatewayResult("not_configured"));
}
public sealed class FiscalOutbox(LumaDbContext db)
{
    public async Task ObserveAsync(BillingPayment payment, CancellationToken ct)
    {
        var row = db.FiscalDocuments.Local.FirstOrDefault(x => x.PaymentId == payment.Id) ?? await db.FiscalDocuments.SingleOrDefaultAsync(x => x.PaymentId == payment.Id, ct);
        if (row is null && payment.Status == "approved")
        {
            row = new() { PaymentId = payment.Id, Amount = payment.Amount, Currency = payment.Currency, PaidAt = payment.ApprovedAt };
            db.FiscalDocuments.Add(row);
        }
        if (row is not null && payment.Status is "refunded" or "charged_back" or "cancelled")
        {
            row.Status = "review_required";
            row.LastError = "Pagamento revertido: análise fiscal necessária; cancelamento financeiro não cancela uma nota.";
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
