using System.Net;
using Luma.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class TwilioTypingIndicatorSenderTests
{
    [Fact]
    public async Task TrySendAsync_PostsTypingIndicatorForWhatsAppMessageSid()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true}")
        });
        var sender = CreateSender(handler);

        var sent = await sender.TrySendAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.True(sent);
        Assert.NotNull(handler.Request);
        Assert.Equal("https://messaging.twilio.com/v2/Indicators/Typing.json", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.NotNull(handler.Request.Headers.Authorization);
        Assert.Contains("messageId=SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", handler.Body);
        Assert.Contains("channel=whatsapp", handler.Body);
    }

    [Fact]
    public async Task TrySendAsync_DoesNotCallTwilioForUnsupportedSid()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sender = CreateSender(handler);

        var sent = await sender.TrySendAsync("IMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.False(sent);
        Assert.Null(handler.Request);
    }

    private static TwilioWhatsAppTypingIndicatorSender CreateSender(CapturingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Twilio:AccountSid"] = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                ["Twilio:AuthToken"] = "auth-token",
                ["Twilio:TypingIndicatorsEnabled"] = "true"
            })
            .Build();

        return new TwilioWhatsAppTypingIndicatorSender(new HttpClient(handler), configuration, NullLogger<TwilioWhatsAppTypingIndicatorSender>.Instance);
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
