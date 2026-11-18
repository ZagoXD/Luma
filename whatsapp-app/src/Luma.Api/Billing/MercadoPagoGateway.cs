using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Luma.Api.Billing;

public sealed class MercadoPagoOptions
{
    public string AccessToken { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string SellerId { get; set; } = "";
    public bool TestMode { get; set; } = true;
}

public sealed record PixDetails(string QrCode, string QrCodeBase64, DateTimeOffset? ExpiresAt);

public sealed class MercadoPagoGateway(HttpClient http, IOptions<MercadoPagoOptions> options) : ISubscriptionGateway
{
    private readonly MercadoPagoOptions settings = options.Value;
    public string Provider => "mercadopago";
    public string PublicKey => settings.PublicKey;
    public bool VerifyWebhook(string resourceId, string requestId, string signature, DateTimeOffset now)
        => MercadoPagoWebhook.Verify(settings.WebhookSecret, resourceId, requestId, signature, now);

    public async Task<ProviderSubscription> CreateAsync(SubscriptionOrder order, string cardToken, CancellationToken ct)
    {
        await ValidateSellerAsync(ct);
        // Recover a timed-out creation by our unguessable external reference before attempting another POST.
        var existing = await FindAsync(order.Id, "card", ct);
        if (existing is not null) return existing;
        var data = await SendAsync(HttpMethod.Post, "preapproval", new
        {
            reason = order.Plan.Name,
            external_reference = order.Id.ToString("N"),
            payer_email = order.Email,
            card_token_id = cardToken,
            back_url = order.ReturnUrl,
            status = "authorized",
            auto_recurring = new { frequency = order.Plan.Months, frequency_type = "months", transaction_amount = order.Plan.Amount, currency_id = "BRL" }
        }, order.Id.ToString("N"), ct);
        return ParseSubscription(data);
    }

    public async Task<ProviderSubscription> CreatePixAsync(SubscriptionOrder order, string cpf, string name, CancellationToken ct)
    {
        await ValidateSellerAsync(ct);
        var existing = await FindAsync(order.Id, "pix", ct);
        if (existing is not null) return existing;
        var data = await SendAsync(HttpMethod.Post, "v1/payments", new
        {
            transaction_amount = order.Plan.Amount,
            description = order.Plan.Name,
            external_reference = order.Id.ToString("N"),
            payment_method_id = "pix",
            payer = new { email = order.Email, first_name = name, identification = new { type = "CPF", number = cpf } },
            metadata = new { billing_interval = order.Plan.Interval },
            date_of_expiration = DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")
        }, order.Id.ToString("N"), ct);
        return ParsePix(data);
    }

    public async Task<PixDetails> GetPixAsync(string id, CancellationToken ct)
    {
        var payment = await SendAsync(HttpMethod.Get, "v1/payments/" + Resource(id[4..]), null, null, ct);
        ValidatePayment(payment);
        var transaction = payment.GetProperty("point_of_interaction").GetProperty("transaction_data");
        return new(Text(transaction, "qr_code"), Text(transaction, "qr_code_base64"), Date(payment, "date_of_expiration"));
    }

    public async Task<ProviderSubscription> GetAsync(string id, CancellationToken ct)
        => id.StartsWith("pix:", StringComparison.Ordinal)
            ? ParsePix(await SendAsync(HttpMethod.Get, "v1/payments/" + Resource(id[4..]), null, null, ct))
            : ParseSubscription(await SendAsync(HttpMethod.Get, "preapproval/" + Resource(id), null, null, ct));

    public async Task<ProviderSubscription?> FindAsync(Guid reference, string paymentMethod, CancellationToken ct)
    {
        var path = paymentMethod == "pix" ? "v1/payments/search" : "preapproval/search";
        var results = await SendAsync(HttpMethod.Get, $"{path}?external_reference={reference:N}", null, null, ct);
        if (!results.TryGetProperty("results", out var items)) return null;
        var matches = items.EnumerateArray().Where(item => Text(item, "external_reference") == reference.ToString("N")).ToArray();
        if (matches.Length > 1) throw new BillingException("Existe mais de uma cobrança para este pedido. Entre em contato com o suporte antes de tentar novamente.", true);
        return matches.Length == 0 ? null : paymentMethod == "pix" ? ParsePix(matches[0]) : ParseSubscription(matches[0]);
    }

    public async Task SetRenewalAsync(string id, bool enabled, CancellationToken ct)
    {
        var expected = enabled ? "authorized" : "paused";
        var response = await SendAsync(HttpMethod.Put, "preapproval/" + Resource(id), new { status = expected }, null, ct);
        if (Text(response, "status") != expected) throw new BillingException("O provedor não confirmou a alteração da renovação.", true);
    }

    public async Task ChangePlanAsync(string id, BillingPlan plan, DateTimeOffset nextPaymentAt, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Put, "preapproval/" + Resource(id), new
        {
            reason = plan.Name,
            auto_recurring = new { transaction_amount = plan.Amount, currency_id = "BRL", frequency = plan.Months, frequency_type = "months" }
        }, null, ct);
        var remote = ParseSubscription(response);
        if (remote.Amount != plan.Amount || remote.Months != plan.Months || remote.Currency != "BRL")
            throw new BillingException("O provedor não confirmou a alteração. Consulte o suporte antes de tentar novamente.", true);
    }

    public async Task ChangeCardAsync(string id, string cardToken, CancellationToken ct)
        => _ = await SendAsync(HttpMethod.Put, "preapproval/" + Resource(id), new { card_token_id = cardToken }, null, ct);

    public async Task<IReadOnlyList<ProviderPayment>> GetPaymentsAsync(string id, CancellationToken ct)
    {
        if (id.StartsWith("pix:", StringComparison.Ordinal))
        {
            var payment = await SendAsync(HttpMethod.Get, "v1/payments/" + Resource(id[4..]), null, null, ct);
            ValidatePayment(payment);
            return [ParsePayment(payment, id)];
        }
        var payments = new List<ProviderPayment>();
        for (var offset = 0; offset < 1200; offset += 100)
        {
            var page = await SendAsync(HttpMethod.Get, $"authorized_payments/search?preapproval_id={Resource(id)}&limit=100&offset={offset}", null, null, ct);
            if (!page.TryGetProperty("results", out var items)) break;
            var count = 0;
            foreach (var invoice in items.EnumerateArray())
            {
                count++;
                if (Text(invoice, "preapproval_id") != id) continue;
                if (!invoice.TryGetProperty("payment", out var summary) || string.IsNullOrEmpty(Text(summary, "id"))) continue;
                // Re-read the payment: invoice status alone is insufficient, particularly for refunds/chargebacks.
                var payment = await SendAsync(HttpMethod.Get, "v1/payments/" + Resource(Text(summary, "id")), null, null, ct);
                ValidatePayment(payment);
                payments.Add(ParsePayment(payment, id));
            }
            if (count < 100) break;
        }
        return payments;
    }

    public async Task<string?> ResolveSubscriptionAsync(string topic, string id, CancellationToken ct)
    {
        if (topic == "subscription_preapproval") return Resource(id);
        if (topic == "subscription_authorized_payment")
            return Text(await SendAsync(HttpMethod.Get, "authorized_payments/" + Resource(id), null, null, ct), "preapproval_id");
        if (topic == "payment")
        {
            var payment = await SendAsync(HttpMethod.Get, "v1/payments/" + Resource(id), null, null, ct);
            ValidatePayment(payment);
            if (Text(payment, "payment_method_id") == "pix") return "pix:" + Resource(id);
            var invoices = await SendAsync(HttpMethod.Get, "authorized_payments/search?payment_id=" + Resource(id), null, null, ct);
            return invoices.GetProperty("results").EnumerateArray().Select(item => Text(item, "preapproval_id")).FirstOrDefault();
        }
        return null;
    }

    private void ValidatePayment(JsonElement data)
    {
        if (Text(data, "collector_id") != settings.SellerId
            || !data.TryGetProperty("live_mode", out var live) || live.GetBoolean() == settings.TestMode)
            throw new BillingException("O pagamento não pertence ao ambiente ou vendedor configurado.");
    }

    private async Task ValidateSellerAsync(CancellationToken ct)
    {
        var seller = await SendAsync(HttpMethod.Get, "users/me", null, null, ct);
        var testUser = seller.TryGetProperty("tags", out var tags) && tags.EnumerateArray().Any(tag => tag.GetString() == "test_user");
        if (Text(seller, "id") != settings.SellerId || testUser != settings.TestMode || Text(seller, "site_id") != "MLB")
            throw new BillingException("As credenciais não correspondem ao vendedor ou ambiente configurado.");
    }

    private ProviderSubscription ParseSubscription(JsonElement data)
    {
        if (data.TryGetProperty("collector_id", out _) && Text(data, "collector_id") != settings.SellerId)
            throw new BillingException("Assinatura de outro vendedor.");
        var recurring = data.GetProperty("auto_recurring");
        if (Text(recurring, "frequency_type") != "months") throw new BillingException("Frequência de cobrança incompatível.");
        return new(Text(data, "id"), Text(data, "external_reference"), Text(data, "status"), Amount(recurring), Text(recurring, "currency_id"), recurring.GetProperty("frequency").GetInt32(), Date(data, "next_payment_date"));
    }

    private ProviderSubscription ParsePix(JsonElement payment)
    {
        ValidatePayment(payment);
        var interval = Text(payment.GetProperty("metadata"), "billing_interval");
        return new("pix:" + Text(payment, "id"), Text(payment, "external_reference"), Text(payment, "status") == "approved" ? "manual" : Text(payment, "status"), Amount(payment), Text(payment, "currency_id"), interval == "annual" ? 12 : 1, null);
    }

    private static ProviderPayment ParsePayment(JsonElement data, string subscriptionId)
        => new(Text(data, "id"), subscriptionId, Text(data, "status"), Amount(data), Text(data, "currency_id"), Date(data, "date_approved") ?? DateTimeOffset.MinValue);

    private static decimal Amount(JsonElement data)
        => decimal.Parse(Text(data, "transaction_amount"), System.Globalization.CultureInfo.InvariantCulture);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, string? idempotency, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.AccessToken) || string.IsNullOrWhiteSpace(settings.SellerId))
            throw new BillingException("Pagamentos temporariamente indisponíveis. Tente novamente mais tarde.");
        using var request = new HttpRequestMessage(method, "https://api.mercadopago.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        if (idempotency is not null) request.Headers.Add("X-Idempotency-Key", idempotency);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new BillingException(response.StatusCode == System.Net.HttpStatusCode.BadRequest
                ? "O Mercado Pago não aceitou os dados. Confira o cartão e o e-mail do pagador e tente novamente."
                : "Não foi possível confirmar a operação no Mercado Pago. Consulte seu perfil antes de tentar novamente.", uncertain: (int)response.StatusCode >= 500);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return document.RootElement.Clone();
    }

    private static string Resource(string value)
        => value.Length is > 0 and <= 128 && value.All(char.IsAsciiLetterOrDigit) ? value : throw new BillingException("Identificador de pagamento inválido.");
    private static string Text(JsonElement data, string key)
        => data.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
    private static DateTimeOffset? Date(JsonElement data, string key)
        => DateTimeOffset.TryParse(Text(data, key), out var date) ? date : null;
}
