using System.Net;
using System.Text;
using System.Text.Json;
using Luma.Api.Billing;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class MercadoPagoGatewayTests
{
    [Theory]
    [InlineData(true, "buyer@testuser.com")]
    [InlineData(false, "customer@example.com")]
    public void SandboxEmailNeverOverridesProductionIdentity(bool test, string expected)
    {
        var gateway = new MercadoPagoGateway(new HttpClient(), Options.Create(new MercadoPagoOptions { TestMode=test, TestBuyerEmail="buyer@testuser.com" }));
        Assert.Equal(expected, gateway.CheckoutEmail("customer@example.com"));
    }
    [Fact]
    public async Task FullRefundUsesOriginalPaymentAndStableIdempotencyKey()
    {
        var gateway = Create(request =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Json("""{"id":"123","collector_id":123,"live_mode":true,"payment_method_id":"master","status":"approved","transaction_amount":20}"""));
            Assert.Equal("/v1/payments/123/refunds", request.RequestUri!.AbsolutePath);
            Assert.Equal("stable-key", request.Headers.GetValues("X-Idempotency-Key").Single());
            return Task.FromResult(Json("""{"id":321,"status":"approved","amount":20}"""));
        });
        await gateway.RefundAsync("123", "stable-key", default);
    }
    [Fact]
    public async Task SubscriptionUsesServerPriceAndTokenWithoutRawCardData()
    {
        var id = Guid.NewGuid();
        var requests = new List<(string Path, string Body)>();
        var gateway = Create(async request =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            requests.Add((request.RequestUri!.AbsolutePath, body));
            if (request.Method == HttpMethod.Get) return Json("{\"results\":[]}");
            Assert.Equal(id.ToString("N"), request.Headers.GetValues("X-Idempotency-Key").Single());
            return Json($$$"""{"id":"remote","external_reference":"{{{id:N}}}","status":"authorized","collector_id":123,"auto_recurring":{"frequency":12,"frequency_type":"months","transaction_amount":118.80,"currency_id":"BRL"}}""");
        });
        var response = await gateway.CreateAsync(new(id, "buyer@example.com", BillingCatalog.Get("essencial", "annual"), "https://example.com/perfil"), "opaque", default);
        Assert.Equal("remote", response.Id);
        var payload = JsonDocument.Parse(requests[1].Body).RootElement;
        Assert.Equal("opaque", payload.GetProperty("card_token_id").GetString());
        Assert.Equal(118.80m, payload.GetProperty("auto_recurring").GetProperty("transaction_amount").GetDecimal());
        Assert.DoesNotContain("security_code", requests[1].Body);
        Assert.DoesNotContain("card_number", requests[1].Body);
    }

    [Fact]
    public async Task TimeoutRecoveryReusesExistingAuthorization()
    {
        var id = Guid.NewGuid();
        var gateway = Create(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json($$$"""{"results":[{"id":"existing","external_reference":"{{{id:N}}}","status":"authorized","auto_recurring":{"frequency":1,"frequency_type":"months","transaction_amount":11,"currency_id":"BRL"}}]}"""));
        });
        var remote = await gateway.CreateAsync(new(id, "buyer@example.com", BillingCatalog.Get("basico", "monthly"), "https://example.com/perfil"), "opaque", default);
        Assert.Equal("existing", remote.Id);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(999, true)]
    public async Task ForeignSellerPaymentCannotBeAcceptedInTest(int seller, bool live)
    {
        var gateway = Create(request => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("authorized_payments")
            ? Json("""{"results":[{"preapproval_id":"sub","payment":{"id":123}}]}""")
            : Json($$$"""{"collector_id":{{{seller}}},"payment_method_id":"master","live_mode":{{{live.ToString().ToLowerInvariant()}}}}""")));
        await Assert.ThrowsAsync<BillingException>(() => gateway.GetPaymentsAsync("sub", default));
    }

    [Fact]
    public async Task SubscriptionSandboxAcceptsLiveFlagOnlyWithVerifiedTestSeller()
    {
        var gateway = Create(request => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("authorized_payments")
            ? Json("{\"results\":[{\"preapproval_id\":\"sub\",\"payment\":{\"id\":123}}]}")
            : Json("{\"id\":123,\"collector_id\":123,\"live_mode\":true,\"payment_method_id\":\"master\",\"status\":\"approved\",\"transaction_amount\":11,\"currency_id\":\"BRL\",\"date_approved\":\"2026-11-18T20:00:00Z\"}")));
        Assert.Single(await gateway.GetPaymentsAsync("sub", default));
    }

    [Fact]
    public async Task InvoiceSearchUsesTheProvidersSupportedPageSize()
    {
        var gateway = Create(request => {
            Assert.Contains("limit=10", request.RequestUri!.Query);
            return Task.FromResult(Json("{\"results\":[]}"));
        });
        Assert.Empty(await gateway.GetPaymentsAsync("sub", default));
    }

    [Fact]
    public async Task ProviderErrorsNeverExposeResponseBodiesOrCredentials()
    {
        var gateway = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive-response") }));
        var exception = await Assert.ThrowsAsync<BillingException>(() => gateway.GetAsync("id", default));
        Assert.DoesNotContain("sensitive", exception.Message);
    }

    private static MercadoPagoGateway Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback)
        => new(new HttpClient(new Handler(callback)), Options.Create(new MercadoPagoOptions { AccessToken = "private", SellerId = "123", TestMode = true }));
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => request.RequestUri!.AbsolutePath == "/users/me"
                ? Task.FromResult(Json("{\"id\":123,\"site_id\":\"MLB\",\"tags\":[\"test_user\"]}")) : callback(request);
    }
}
