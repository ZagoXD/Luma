using Luma.Api.Data;
using Luma.Api.Fiscal;
using Microsoft.EntityFrameworkCore;

namespace Luma.Tests;

public sealed class FiscalProcessorTests
{
    [Fact]
    public async Task Does_not_issue_before_accounting_approval_and_reconciles_timeout_without_reissuing()
    {
        await using var db = new LumaDbContext(new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var row = new FiscalDocument { PaymentId = "mercadopago:test", Amount = 70.8m };
        db.FiscalDocuments.Add(row);
        await db.SaveChangesAsync();
        var gateway = new Gateway();
        var processor = new FiscalProcessor(db, gateway);
        await processor.ProcessAsync(row.Id, false, default);
        Assert.Equal(0, gateway.Issues);
        await processor.ProcessAsync(row.Id, true, default);
        Assert.Equal("awaiting_reconciliation", row.Status);
        Assert.Null(row.Number);
        row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await processor.ProcessAsync(row.Id, true, default);
        Assert.Equal(1, gateway.Issues);
        Assert.Equal(1, gateway.Queries);
        Assert.Equal("authorized", row.Status);
        await processor.ProcessAsync(row.Id, true, default);
        Assert.Equal(1, gateway.Issues);
    }

    private sealed class Gateway : IFiscalGateway
    {
        public int Issues; public int Queries;
        public Task<FiscalGatewayResult> IssueAsync(FiscalIssueRequest request, CancellationToken ct)
        { Issues++; throw new HttpRequestException("Simulated uncertain outcome"); }
        public Task<FiscalGatewayResult> QueryAsync(string idempotencyKey, CancellationToken ct)
        { Queries++; return Task.FromResult(new FiscalGatewayResult("authorized", "test-only", "TEST", "TEST-KEY")); }
        public Task<FiscalGatewayResult> RequestCancellationAsync(string providerId, string reason, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
