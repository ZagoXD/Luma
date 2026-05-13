using System.Net;
using System.Text.Json;
using Luma.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class EmailServiceTests
{
    [Fact]
    public async Task SendWelcomeEmailAsync_SendsTemplateWithConfiguredVariables()
    {
        var handler = new CapturingHandler("""{"id":"email_123"}""");
        var service = new ResendEmailService(
            new HttpClient(handler),
            Options.Create(new ResendOptions { ApiKey = "re_test" }),
            Options.Create(new EmailOptions { From = "Luma <noreply@ia-luma.com.br>", WebBaseUrl = "https://ia-luma.com.br" }),
            Options.Create(new EmailTemplateOptions { Welcome = "tpl_welcome" }),
            NullLogger<ResendEmailService>.Instance);

        var result = await service.SendWelcomeEmailAsync("nay@example.com", "Nayara");

        Assert.True(result.Success);
        Assert.Equal("https://api.resend.com/emails", handler.RequestUri);
        Assert.Equal("Bearer re_test", handler.Authorization);
        var template = handler.Json.RootElement.GetProperty("template");
        Assert.Equal("tpl_welcome", template.GetProperty("id").GetString());
        Assert.Equal("Luma <noreply@ia-luma.com.br>", handler.Json.RootElement.GetProperty("from").GetString());
        Assert.Equal("nay@example.com", handler.Json.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Nayara", template.GetProperty("variables").GetProperty("userName").GetString());
        Assert.Equal("https://ia-luma.com.br/login", template.GetProperty("variables").GetProperty("loginUrl").GetString());
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_DoesNotSendWhenTemplateIsMissing()
    {
        var handler = new CapturingHandler("""{"id":"email_123"}""");
        var service = new ResendEmailService(
            new HttpClient(handler),
            Options.Create(new ResendOptions { ApiKey = "re_test" }),
            Options.Create(new EmailOptions()),
            Options.Create(new EmailTemplateOptions()),
            NullLogger<ResendEmailService>.Instance);

        var result = await service.SendPasswordResetEmailAsync("nay@example.com", "Nayara", "https://ia-luma.com.br/reset-password?token=abc", 30);

        Assert.False(result.Success);
        Assert.Null(handler.JsonDocument);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public JsonDocument? JsonDocument { get; private set; }
        public JsonDocument Json => JsonDocument ?? throw new InvalidOperationException("No request captured.");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            JsonDocument = JsonDocument.Parse(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
