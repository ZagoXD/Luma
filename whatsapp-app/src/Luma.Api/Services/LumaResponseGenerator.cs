using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luma.Api.Models;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public interface ILumaResponseGenerator
{
    Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default);
}

public sealed record LumaResponseRequest(
    string UserMessage,
    string BackendResult,
    string OnboardingStep,
    string? DisplayName,
    string? PendingAction,
    bool IsGuardrail,
    IReadOnlyList<string> AvailableTools,
    string? Knowledge);

public sealed class PassthroughLumaResponseGenerator : ILumaResponseGenerator
{
    public Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.BackendResult);
    }
}

public sealed class OllamaLumaResponseGenerator(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLumaResponseGenerator> logger) : ILumaResponseGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OllamaOptions _options = options.Value;

    public async Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || request.IsGuardrail || string.IsNullOrWhiteSpace(request.BackendResult))
        {
            return request.BackendResult;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, _options.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var prompt = $$"""
Voce e a Luma, uma assistente de ciclo menstrual e gravidez pelo WhatsApp.

Personalidade:
- acolhedora, clara, humana e breve
- fala em portugues do Brasil
- usa no maximo 2 paragrafos curtos, exceto quando precisar listar opcoes do cadastro
- nao usa markdown pesado
- nao inventa dados e nao promete diagnostico

Limites fixos:
- nao confirma gravidez
- nao diz se sangramento e normal
- nao prescreve tratamento
- nao substitui medico, ginecologista, obstetra ou pre-natal
- nao grava nada diretamente; o backend ja executou ou decidiu a acao autorizada

Contexto da conversa:
- nome conhecido: {{request.DisplayName ?? "desconhecido"}}
- etapa atual: {{request.OnboardingStep}}
- acao pendente: {{request.PendingAction ?? "nenhuma"}}

Ferramentas/backends disponiveis:
{{string.Join(", ", request.AvailableTools)}}

Base RAG relevante:
{{request.Knowledge ?? "Sem trecho adicional."}}

Mensagem da usuaria:
{{request.UserMessage}}

Resultado autoritativo do backend:
{{request.BackendResult}}

Tarefa:
Escreva a resposta final como a Luma. Preserve todos os fatos, datas, perguntas obrigatorias, opcoes numeradas e limites do resultado do backend. Pode humanizar o tom, mas nao remova consentimento, alertas medicos, pedidos de confirmacao ou proximas perguntas.

Responda somente JSON valido:
{
  "reply": "texto final"
}
""";

        try
        {
            var response = await httpClient.PostAsJsonAsync(
                new Uri(new Uri(_options.BaseUrl), "/api/generate"),
                new
                {
                    model = _options.Model,
                    prompt,
                    stream = false,
                    format = "json",
                    options = new { temperature = 0.15 }
                },
                SerializerOptions,
                linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama response generation failed with status {StatusCode}", response.StatusCode);
                return request.BackendResult;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(SerializerOptions, linked.Token);
            if (string.IsNullOrWhiteSpace(payload?.Response))
            {
                return request.BackendResult;
            }

            var generated = JsonSerializer.Deserialize<OllamaLumaReply>(payload.Response, SerializerOptions);
            if (string.IsNullOrWhiteSpace(generated?.Reply))
            {
                return request.BackendResult;
            }

            var reply = generated.Reply.Trim();
            return PreservesRequiredBackendContent(request.BackendResult, reply)
                ? reply
                : request.BackendResult;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Ollama response generation unavailable; using backend result.");
            return request.BackendResult;
        }
    }

    private static bool PreservesRequiredBackendContent(string backendResult, string generatedReply)
    {
        var backend = MessageText.Normalize(backendResult);
        var reply = MessageText.Normalize(generatedReply);

        if (backend.Contains("voce aceita", StringComparison.Ordinal))
        {
            return reply.Contains("aceita", StringComparison.Ordinal)
                && reply.Contains("1.", StringComparison.Ordinal)
                && reply.Contains("2.", StringComparison.Ordinal);
        }

        if (backend.Contains("como devo te chamar", StringComparison.Ordinal)
            || backend.Contains("como devo chamar", StringComparison.Ordinal))
        {
            return reply.Contains("como devo te chamar", StringComparison.Ordinal)
                || reply.Contains("como posso te chamar", StringComparison.Ordinal)
                || reply.Contains("qual e o seu nome", StringComparison.Ordinal)
                || reply.Contains("seu nome", StringComparison.Ordinal)
                || reply.Contains("seu apelido", StringComparison.Ordinal);
        }

        if (backend.Contains("18 anos", StringComparison.Ordinal))
        {
            return reply.Contains("18", StringComparison.Ordinal);
        }

        if (backend.Contains("ultima menstruacao", StringComparison.Ordinal))
        {
            return reply.Contains("ultima menstruacao", StringComparison.Ordinal)
                && (reply.Contains("dia", StringComparison.Ordinal) || reply.Contains("data", StringComparison.Ordinal));
        }

        if (backend.Contains("ciclo costuma ter quantos dias", StringComparison.Ordinal))
        {
            return reply.Contains("ciclo", StringComparison.Ordinal)
                && reply.Contains("dias", StringComparison.Ordinal);
        }

        if (backend.Contains("costuma durar quantos dias", StringComparison.Ordinal)
            || backend.Contains("duracao media da menstruacao", StringComparison.Ordinal))
        {
            return (reply.Contains("durar", StringComparison.Ordinal) || reply.Contains("duracao", StringComparison.Ordinal))
                && reply.Contains("dias", StringComparison.Ordinal);
        }

        if (backend.Contains("metodo contraceptivo", StringComparison.Ordinal))
        {
            return reply.Contains("contracept", StringComparison.Ordinal);
        }

        if (backend.Contains("quer que eu registre isso agora", StringComparison.Ordinal))
        {
            return reply.Contains("registr", StringComparison.Ordinal)
                && (reply.Contains("sim", StringComparison.Ordinal) || reply.Contains("1.", StringComparison.Ordinal));
        }

        if (backend.Contains("como esta o fluxo", StringComparison.Ordinal))
        {
            return reply.Contains("fluxo", StringComparison.Ordinal);
        }

        return true;
    }

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }

    private sealed class OllamaLumaReply
    {
        public string? Reply { get; set; }
    }
}

public static class LumaTools
{
    public static readonly IReadOnlyList<string> Available =
    [
        "get_user_profile: le nome, preferencias e dados basicos autorizados da usuaria",
        "get_onboarding_state: le etapa atual do cadastro e pendencias",
        "save_pending_intent: guarda intencao fora de ordem para confirmar depois",
        "complete_onboarding_step: avanca uma etapa validada do cadastro",
        "record_period_start: registra inicio de menstruacao validado pelo backend",
        "record_period_end: registra termino de menstruacao validado pelo backend",
        "record_flow_update: registra fluxo leve, medio, intenso ou nao informado",
        "record_symptom: registra sintomas do ciclo sem diagnosticar",
        "record_mood: registra humor para historico",
        "record_sexual_activity: registra relacao sexual para historico",
        "start_pregnancy_mode: inicia acompanhamento de gravidez informado pela usuaria",
        "record_pregnancy_bleeding: registra sangramento na gravidez e aciona orientacao segura",
        "record_pregnancy_symptom: registra sintoma de gravidez",
        "record_prenatal_appointment: registra consulta de pre-natal ou obstetra",
        "record_ultrasound: registra ultrassom",
        "calculate_next_period: calcula estimativa de proxima menstruacao",
        "calculate_delay: calcula estimativa de atraso menstrual",
        "get_last_period: consulta ultima menstruacao registrada",
        "get_last_symptom: consulta ultimo sintoma registrado",
        "get_last_sexual_activity: consulta ultima relacao sexual registrada",
        "search_luma_knowledge_base: busca trechos RAG seguros sobre Luma, ciclo, gravidez, privacidade e limites medicos"
    ];
}
