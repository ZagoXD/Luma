using Luma.Api.Data;
using Luma.Api.Fiscal;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Tests;

public sealed class FiscalPreparationTests
{
    [Fact]
    public async Task Paid_event_is_unique_and_never_claims_to_be_an_invoice()
    {
        await using var db = new LumaDbContext(new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var payment = new BillingPayment { Id = "mercadopago:test", AccountSubscriptionId = Guid.NewGuid(), Status = "approved", Amount = 70.8m, ApprovedAt = DateTimeOffset.UtcNow, PeriodEndsAt = DateTimeOffset.UtcNow.AddYears(1) };
        var service = new FiscalOutbox(db);
        payment.Status = "pending";
        await service.ObserveAsync(payment, default); await db.SaveChangesAsync();
        Assert.Empty(await db.FiscalDocuments.ToListAsync());
        payment.Status = "approved";
        await service.ObserveAsync(payment, default); await db.SaveChangesAsync();
        await service.ObserveAsync(payment, default); await db.SaveChangesAsync();
        var row = Assert.Single(await db.FiscalDocuments.ToListAsync());
        Assert.Equal("awaiting_configuration", row.Status); Assert.Null(row.Number); Assert.Null(row.AccessKey);
        payment.Status = "refunded"; await service.ObserveAsync(payment, default); await db.SaveChangesAsync();
        Assert.Equal("review_required", row.Status);
    }
    [Fact]
    public async Task Disabled_adapter_cannot_issue_a_real_or_fake_authorized_note()
    {
        var result = await new DisabledFiscalGateway().IssueAsync(new FiscalIssueRequest("test", 100m, "BRL", DateTimeOffset.UtcNow), default);
        Assert.Equal("not_configured", result.Status); Assert.Null(result.Number);
    }
}
