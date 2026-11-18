using System.Net;
using Luma.Api.Services;
using Luma.Api.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class TwilioNotificationSenderTests
{
    [Fact]
    public async Task Rate_limit_preserves_the_providers_retry_after_hint()
    {
        var result = await Create(new Handler(429, "{}", 180)).SendTemplateAsync("+5511999990001",
            NotificationTypes.ContraceptiveDaily, new Dictionary<string, string>());
        Assert.Equal(TimeSpan.FromSeconds(180), result.RetryAfter);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    public async Task Sender_classifies_provider_rejections_without_logging_the_message_body(int status, bool retryable)
    {
        var handler = new Handler(status, "{\"code\":63016,\"message\":\"private data\"}");
        var result = await Create(handler).SendTemplateAsync("+5511999990001", NotificationTypes.ContraceptiveDaily, new Dictionary<string, string>());
        Assert.False(result.Success);
        Assert.Equal(retryable, result.Retryable);
        Assert.DoesNotContain("private data", result.ErrorMessage);
    }

    [Fact]
    public async Task Scheduled_send_includes_the_signed_callback_route_and_preserves_template_variables()
    {
        var handler = new Handler(201, "{\"sid\":\"SMtest\",\"status\":\"queued\"}");
        var id = Guid.NewGuid();
        var result = await Create(handler).SendScheduledTemplateAsync("+5511999990001", NotificationTypes.ContraceptiveDaily,
            new Dictionary<string, string> { ["1"] = "Nay", ["2"] = "07:00" }, id);
        Assert.True(result.Success);
        Assert.Contains(Uri.EscapeDataString("https://api.ia-luma.com.br/webhooks/twilio/notifications/status/" + id), handler.Body);
        Assert.Contains("ContentSid=HXpill", handler.Body);
        Assert.Contains("ContentVariables=", handler.Body);
    }

    [Fact]
    public async Task Successful_http_without_a_message_id_is_ambiguous_not_a_safe_retry()
    {
        var result = await Create(new Handler(201, "{}")).SendTemplateAsync("+5511999990001", NotificationTypes.ContraceptiveDaily, new Dictionary<string, string>());
        Assert.False(result.Success);
        Assert.True(result.DeliveryUnknown);
        Assert.False(result.Retryable);
    }

    private static TwilioWhatsAppNotificationSender Create(Handler handler) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Twilio:AccountSid"] = "ACtest", ["Twilio:AuthToken"] = "test", ["Twilio:WhatsAppFrom"] = "whatsapp:+16204008668",
            ["Twilio:TemplateContraceptiveDaily"] = "HXpill", ["Twilio:NotificationStatusCallbackBaseUrl"] = "https://api.ia-luma.com.br/webhooks/twilio/notifications/status"
        }).Build(), NullLogger<TwilioWhatsAppNotificationSender>.Instance);

    private sealed class Handler(int status, string content, int? retryAfter = null) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(content) };
            if (retryAfter is not null) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfter.Value));
            return response;
        }
    }
}
