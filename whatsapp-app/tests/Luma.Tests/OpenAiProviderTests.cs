using System.Net;
using System.Text;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class OpenAiProviderTests
{
    [Fact]
    public async Task OpenAi_tool_agent_sends_api_key_and_parses_structured_tool_call()
    {
        var handler = new StubHttpMessageHandler("""
{
  "output": [
    {
      "type": "message",
      "content": [
        {
          "type": "output_text",
          "text": "{\"tool_name\":\"complete_onboarding_step\",\"display_name\":null,\"consent_accepted\":true,\"is_adult_confirmed\":null,\"date\":null,\"average_cycle_length\":null,\"average_period_length\":null,\"contraceptive_type\":null,\"flow_intensity\":null,\"symptom\":null,\"intensity\":null,\"mood\":null,\"protected\":null,\"gestational_weeks\":null,\"last_period_date\":null,\"estimated_due_date\":null,\"confidence\":0.96}"
        }
      ]
    }
  ]
}
""");
        var http = new HttpClient(handler);
        var options = Options.Create(new OpenAiOptions
        {
            ApiKey = "test-openai-key",
            BaseUrl = "https://api.openai.test/v1",
            Model = "gpt-5.4-mini"
        });
        var client = new OpenAiResponsesClient(http, options, NullLogger<OpenAiResponsesClient>.Instance);
        var agent = new OpenAiLumaToolAgent(client);

        var tool = await agent.DecideAsync(new LumaToolAgentRequest(
            UserMessage: "Aceito sim",
            Today: new DateOnly(2026, 4, 29),
            Context: new ConversationContext
            {
                DisplayName = null,
                OnboardingStep = OnboardingSteps.AwaitingConsent,
                HasAcceptedConsent = false,
                HasCompletedOnboarding = false,
                PendingAction = null
            },
            Knowledge: null,
            AvailableTools: LumaTools.Available));

        Assert.NotNull(tool);
        Assert.Equal("complete_onboarding_step", tool.ToolName);
        Assert.True(tool.ConsentAccepted);
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("test-openai-key", handler.LastRequest?.Headers.Authorization?.Parameter);
        Assert.Equal("https://api.openai.test/v1/responses", handler.LastRequest?.RequestUri?.ToString());
        Assert.Contains("json_schema", handler.LastRequestBody);
        Assert.Contains("luma_tool_call", handler.LastRequestBody);
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
