using System.Net;
using System.Text.Json;
using Luma.Api.Services;
using Luma.Api.Services.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Luma.Tests;

public sealed class AssistantOrchestratorTests
{
    [Fact]
    public async Task Failed_generation_never_returns_raw_retrieved_documents()
    {
        var model = new CallsThenTextModel("search_luma_knowledge_base", new { query = "ciclo" }, "", failAfterCall: true);
        var reply = await Create(model).RespondAsync(new("pergunta", "estado", new(2026, 11, 10), ""),
            (_, _, _) => Task.FromResult(new ToolOutcome(true, "TEXTO INTEGRAL DO ACERVO", ContextOnly: true)));
        Assert.DoesNotContain("TEXTO INTEGRAL", reply.Body);
        Assert.Contains("Não tenho informação segura", reply.Body);
    }

    [Fact]
    public async Task Internal_knowledge_never_creates_a_source_block_even_if_model_invents_one()
    {
        var model = new CallsThenTextModel("search_luma_knowledge_base", new { query = "plano" },
            "O Básico tem texto.\n\nFontes: https://internal.example/planos");
        var reply = await Create(model).RespondAsync(new("pergunta", "estado", new(2026, 11, 10), ""),
            (_, _, _) => Task.FromResult(new ToolOutcome(true, "Plano Básico tem texto.", Sources: [])));
        Assert.DoesNotContain("internal.example", reply.Body);
        Assert.DoesNotContain("Fontes:", reply.Body);
    }

    [Fact]
    public async Task Unknown_tool_from_model_fails_closed_without_breaking_the_webhook()
    {
        var executions = 0;
        var model = new CallsThenTextModel("delete_all_users", new { }, "Apaguei tudo.");
        var reply = await Create(model).RespondAsync(new("pergunta", "estado", new(2026, 11, 10), ""),
            (_, _, _) => { executions++; return Task.FromResult(new ToolOutcome(true, "unexpected")); });
        Assert.Equal(0, executions);
        Assert.DoesNotContain("Apaguei", reply.Body);
        Assert.NotEmpty(reply.Body);
    }

    [Fact]
    public async Task Backend_replaces_model_source_block_with_verified_sources_only()
    {
        var model = new CallsThenTextModel("search_luma_knowledge_base", new { query = "ciclo" },
            "São estimativas.\n\nFontes: fonte inventada https://example.com/inventada");
        var reply = await Create(model).RespondAsync(new("pergunta", "estado", new(2026, 11, 10), ""),
            (_, _, _) => Task.FromResult(new ToolOutcome(true, "Estimativas.", Sources: [new("cycle", "Ciclo", "https://www.gov.br/saude", "v1")])));
        Assert.DoesNotContain("inventada", reply.Body);
        Assert.Equal(1, reply.Body.Split("Fontes:").Length - 1);
        Assert.Contains("https://www.gov.br/saude", reply.Body);
    }

    [Fact]
    public async Task Sources_and_calendar_links_are_not_changed_by_accent_correction()
    {
        const string url = "https://www.gov.br/saude/menstruacao";
        const string calendar = "https://ia-luma.com.br/perfil/calendario?month=2026-11";
        var model = new CallsThenTextModel("search_luma_knowledge_base", new { query = "calendário" }, "Sua proxima menstruacao e uma estimativa. " + calendar);
        var reply = await Create(model).RespondAsync(new("pergunta", "cadastro completo", new(2026, 11, 10), ""),
            (_, _, _) => Task.FromResult(new ToolOutcome(true, "Previsões são estimativas.", Sources: [new("calendar", "Calendário", url, "v1")])));
        Assert.Contains(url, reply.Body);
        Assert.Contains(calendar, reply.Body);
        Assert.DoesNotContain("/saúde", reply.Body);
    }

    [Fact]
    public async Task Missing_evidence_cannot_be_replaced_with_an_invented_final_answer()
    {
        var model = new CallsThenTextModel("search_luma_knowledge_base", new { query = "sem fonte" }, "É normal, não precisa de médico.");
        var reply = await Create(model).RespondAsync(new("pergunta", "estado", new(2026, 11, 10), ""),
            (_, _, _) => Task.FromResult(new ToolOutcome(false, "Não encontrei uma fonte revisada.")));
        Assert.Equal("Não encontrei uma fonte revisada.", reply.Body);
    }

    [Fact]
    public async Task Responses_payload_uses_real_function_tools_and_disables_provider_storage()
    {
        var handler = new CaptureHandler();
        var model = new OpenAiAssistantModel(new HttpClient(handler), Options.Create(new OpenAiOptions { ApiKey = "test-key" }),
            NullLogger<OpenAiAssistantModel>.Instance);
        await model.CompleteAsync([new { role = "user", content = "pergunta sintética" }], LumaToolCatalog.Tools, true, default);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.False(json.RootElement.GetProperty("store").GetBoolean());
        Assert.False(json.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal("required", json.RootElement.GetProperty("tool_choice").GetString());
        Assert.All(json.RootElement.GetProperty("tools").EnumerateArray(), tool => Assert.Equal("function", tool.GetProperty("type").GetString()));
        Assert.DoesNotContain("user_id", handler.Body);
    }

    private static AssistantOrchestrator Create(IAssistantModel model) => new(model,
        Options.Create(new AssistantOptions { Enabled = true }), NullLogger<AssistantOrchestrator>.Instance);

    private sealed class CallsThenTextModel(string name, object arguments, string text, bool failAfterCall = false) : IAssistantModel
    {
        private bool _called;
        public Task<AssistantModelTurn> CompleteAsync(IReadOnlyList<object> input, IReadOnlyList<Tool> tools, bool requireTool, CancellationToken ct)
        {
            if (_called)
            {
                if (failAfterCall) throw new HttpRequestException("simulated");
                return Task.FromResult(new AssistantModelTurn(text, []));
            }
            _called = true;
            return Task.FromResult(new AssistantModelTurn(null, [JsonSerializer.SerializeToElement(new
            {
                type = "function_call", name, call_id = "call_1", arguments = JsonSerializer.Serialize(arguments)
            })]));
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"status":"completed","output":[]}""") };
        }
    }
}
