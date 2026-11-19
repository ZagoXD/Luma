using System.Text.Json;
using System.Text.Json.Serialization;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Billing;

public static class BillingEndpoints
{
    public static void MapBilling(this WebApplication app, Func<HttpRequest, LumaDbContext, Task<AccountUser?>> authenticate)
    {
        var group = app.MapGroup("").AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (BillingException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            { return Results.Json(new { message = "Não consegui confirmar o pagamento agora. Consulte seu perfil antes de tentar novamente." }, statusCode: 503); }
        });

        group.MapPost("/checkout/create-subscription", async (HttpRequest http, CheckoutRequest input, LumaDbContext db,
            BillingService billing, ISubscriptionGateway gateway, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            if (!gateway.IsReady)
                return Results.Json(new { message = "O checkout está temporariamente indisponível." }, statusCode: 503);
            var local = await billing.PrepareAsync(user, input.PlanCode, input.BillingInterval, ct, input.ReplacesSubscriptionId);
            return Results.Ok(new { provider = gateway.Provider, publicKey = gateway.PublicKey, checkoutId = local.Id,
                amount = BillingCatalog.Get(local.PlanCode, local.BillingInterval).Amount, email = user.Email,
                submitted = local.PaymentAttemptedAt is not null, scheduled = local.ReplacementConfirmedAt != null, startsAt = local.StartsAt });
        });

        group.MapPost("/checkout/confirm-subscription", async (HttpRequest http, PaymentRequest input, LumaDbContext db,
            BillingService billing, ISubscriptionGateway gateway, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            var local = await billing.SubmitAsync(user, input.CheckoutId, input.PaymentToken, input.PaymentMethod, input.PayerEmail, ct);
            return Results.Ok(new { subscription = View(local) });
        });

        group.MapGet("/checkout/status/{id:guid}", async (Guid id, HttpRequest http, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            return user is null ? Results.Unauthorized() : Results.Ok(new { subscription = View(await billing.RefreshAsync(user.Id, id, ct)) });
        });

        group.MapPost("/account/subscription/cancel", async (HttpRequest http, ScheduledRequest input, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            return user is null ? Results.Unauthorized() : Results.Ok(new { subscription = View(await billing.TerminateAsync(user.Id, input.Id, false, ct)) });
        });
        group.MapPost("/account/subscription/renewal", async (HttpRequest http, RenewalRequest input, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            return user is null ? Results.Unauthorized() : Results.Ok(new { subscription = View(await billing.SetRenewalAsync(user.Id, input.Enabled, ct)) });
        });
        group.MapPost("/account/subscription/withdraw", async (HttpRequest http, ScheduledRequest input, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            return user is null ? Results.Unauthorized() : Results.Ok(new { subscription = View(await billing.TerminateAsync(user.Id, input.Id, true, ct)) });
        });
        group.MapGet("/account/subscription/withdrawals", async (HttpRequest http, LumaDbContext db, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            var orders = await db.AccountSubscriptions.Where(s => s.AccountUserId == user.Id).OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct);
            var ids = orders.Select(s => s.Id).ToArray();
            var payments = await db.BillingPayments.Where(p => ids.Contains(p.AccountSubscriptionId)).ToListAsync(ct);
            return Results.Ok(new { orders = orders.Select(s => new { s.Id, s.PlanCode, s.BillingInterval, s.Status, s.WithdrawalRequestedAt, s.WithdrawalCompletedAt,
                deadline = payments.Where(p => p.AccountSubscriptionId == s.Id).Select(p => (DateTimeOffset?)p.ApprovedAt).Min()?.AddDays(7) }) });
        });
        group.MapPost("/account/subscription/resume", async (HttpRequest http, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            return user is null ? Results.Unauthorized() : Results.Ok(new { subscription = View(await billing.SetRenewalAsync(user.Id, true, ct)) });
        });
        group.MapPost("/account/subscription/change-plan", async (HttpRequest http, CheckoutRequest input, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            return user is null ? Results.Unauthorized() : Results.Ok(new { subscription = View(await billing.ChangePlanAsync(user.Id, input.PlanCode, input.BillingInterval, ct)) });
        });
        group.MapPost("/account/subscription/scheduled/cancel", async (HttpRequest http, ScheduledRequest input, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            await billing.CancelScheduledAsync(user.Id, input.Id, ct);
            return Results.Ok(new { ok = true });
        });
        group.MapGet("/account/payment-method/config", async (HttpRequest http, LumaDbContext db, ISubscriptionGateway gateway) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            var current = await db.AccountSubscriptions.Where(s => s.AccountUserId == user.Id && s.BillingProvider == gateway.Provider
                && s.PaymentMethod == "card" && s.CurrentPeriodEndsAt > DateTimeOffset.UtcNow
                && (s.Status == "active" || s.Status == "canceled")).OrderByDescending(s => s.CurrentPeriodEndsAt).FirstOrDefaultAsync();
            if (current is null) throw new BillingException("Nenhuma assinatura disponível para atualizar o cartão.");
            return Results.Ok(new { publicKey = gateway.PublicKey, email = user.Email, amount = BillingCatalog.Get(current.PlanCode, current.BillingInterval).Amount });
        });
        group.MapPost("/account/payment-method/confirm", async (HttpRequest http, CardRequest input, LumaDbContext db, BillingService billing, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            await billing.ChangeCardAsync(user.Id, input.PaymentToken, ct);
            return Results.Ok(new { ok = true });
        });
        group.MapGet("/account/billing/transactions", async (HttpRequest http, LumaDbContext db, CancellationToken ct) =>
        {
            var user = await authenticate(http, db);
            if (user is null) return Results.Unauthorized();
            var transactions = await (from payment in db.BillingPayments
                join subscription in db.AccountSubscriptions on payment.AccountSubscriptionId equals subscription.Id
                where subscription.AccountUserId == user.Id
                orderby payment.ApprovedAt descending
                select new { payment.Id, payment.Status, payment.Currency, amountPaid = payment.Amount * 100, amountDue = 0,
                    created = payment.ApprovedAt }).Take(100).ToListAsync(ct);
            return Results.Ok(new { transactions });
        });
        group.MapPost("/webhooks/mercadopago", async (HttpRequest request, ISubscriptionGateway gateway, BillingService billing, CancellationToken ct) =>
        {
            var id = request.Query["data.id"].ToString();
            if (!gateway.VerifyWebhook(id, request.Headers["x-request-id"].ToString(), request.Headers["x-signature"].ToString(), DateTimeOffset.UtcNow))
                return Results.Unauthorized();
            using var payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            if (!payload.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("id", out var bodyId)
                || !string.Equals(bodyId.ToString(), id, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest();
            var topic = payload.RootElement.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "";
            await billing.ReceiveAsync(topic, id, ct);
            return Results.Ok(new { received = true });
        });
    }

    public static object View(AccountSubscription subscription) => new
    {
        subscription.Id, subscription.PlanCode, planName = subscription.PlanCode == "essencial" ? "Essencial" : "Básico",
        subscription.BillingInterval, billingLabel = subscription.BillingInterval == "annual" ? "Anual" : "Mensal",
        subscription.Status, subscription.StartsAt, subscription.CurrentPeriodEndsAt, subscription.CanceledAt,
        subscription.PaymentMethod, subscription.PendingPlanCode, subscription.PendingBillingInterval,
        subscription.TerminatedAt, subscription.WithdrawalRequestedAt, subscription.WithdrawalCompletedAt,
        scheduled = subscription.Status == "pending" && subscription.ReplacementConfirmedAt != null && subscription.StartsAt > DateTimeOffset.UtcNow,
        canManage = subscription.TerminatedAt == null && subscription.BillingProvider != "legacy" && subscription.PaymentMethod == "card" && subscription.ReplacedBySubscriptionId == null
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckoutRequest(string PlanCode, string BillingInterval, Guid? ReplacesSubscriptionId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentRequest(Guid CheckoutId, string? PaymentToken, string PaymentMethod, string? PayerEmail);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CardRequest(string PaymentToken);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScheduledRequest(Guid Id);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenewalRequest(bool Enabled);
