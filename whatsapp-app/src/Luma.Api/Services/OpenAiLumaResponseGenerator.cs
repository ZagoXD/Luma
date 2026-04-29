namespace Luma.Api.Services;

public sealed class OpenAiLumaResponseGenerator(OpenAiResponsesClient openAi) : ILumaResponseGenerator
{
    public async Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default)
    {
        if (request.IsGuardrail || string.IsNullOrWhiteSpace(request.BackendResult))
        {
            return request.BackendResult;
        }

        var generated = await openAi.CreateStructuredAsync<OpenAiLumaReply>(
            "luma_final_reply",
            OpenAiJsonSchemas.LumaReply,
            """
Você é a Luma, uma assistente de ciclo menstrual e gravidez pelo WhatsApp.
Escreva em portugues do Brasil, com tom acolhedor, humano e breve. Não invente dados, não diagnostique, não prescreva e não remova limites medicos ou consentimento.
""",
            $$"""
Contexto:
- nome conhecido: {{request.DisplayName ?? "desconhecido"}}
- etapa atual: {{request.OnboardingStep}}
- acao pendente: {{request.PendingAction ?? "nenhuma"}}

Ferramentas/backend disponiveis:
{{string.Join("\n", request.AvailableTools)}}

Base RAG relevante:
{{request.Knowledge ?? "Sem trecho adicional."}}

Mensagem da usuaria:
{{request.UserMessage}}

Resultado autoritativo do backend:
{{request.BackendResult}}

Tarefa:
Escreva a resposta final como a Luma. Preserve todos os fatos, datas, perguntas obrigatorias, opcoes numeradas, pedidos de confirmacao e alertas do backend.
""",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(generated?.Reply))
        {
            return request.BackendResult;
        }

        var reply = generated.Reply.Trim();
        return PreservesRequiredBackendContent(request.BackendResult, reply)
            ? reply
            : request.BackendResult;
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

    private sealed class OpenAiLumaReply
    {
        public string? Reply { get; set; }
    }
}
