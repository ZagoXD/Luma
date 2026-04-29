using System.Text.Json.Serialization;
using Luma.Api.Models;

namespace Luma.Api.Services;

public sealed class OpenAiLumaToolAgent(OpenAiResponsesClient openAi) : ILumaToolAgent
{
    public async Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return null;
        }

        var result = await openAi.CreateStructuredAsync<OpenAiToolCall>(
            "luma_tool_call",
            OpenAiJsonSchemas.ToolCall,
            """
Você é a Luma agente. Escolha uma unica tool para o backend validar e executar.
Não grave dados, não diagnostique e não responda em linguagem natural. Se a mensagem for fora do escopo, use out_of_scope. Se pedir diagnostico/confirmacao medica, use medical_guardrail.
""",
            $$"""
Hoje: {{request.Today:yyyy-MM-dd}}
Etapa: {{request.Context.OnboardingStep}}
Cadastro completo: {{request.Context.HasCompletedOnboarding}}
Consentimento aceito: {{request.Context.HasAcceptedConsent}}
Pendencia: {{request.Context.PendingAction ?? "nenhuma"}}
Nome conhecido: {{request.Context.DisplayName ?? "desconhecido"}}

Tools disponiveis:
{{string.Join("\n", request.AvailableTools)}}

Base RAG relevante:
{{request.Knowledge ?? "Sem trecho adicional."}}

Regras:
- awaiting_consent + concordancia ("claro", "pode seguir", "perfeitamente", "sim", "aceito") => complete_onboarding_step com consent_accepted=true.
- awaiting_display_name/age/date/cycle/period_length/contraceptive => complete_onboarding_step com campos encontrados.
- menstruacao iniciou => record_period_start.
- menstruacao terminou => record_period_end.
- fluxo => record_flow_update.
- dor/sintoma => record_symptom.
- humor => record_mood.
- relacao/sexo/intimidade sexual => record_sexual_activity.
- teste positivo/estou gravida => start_pregnancy_mode, sem confirmar diagnostico.
- sangramento na gravidez => record_pregnancy_bleeding.
- "quando e minha proxima menstruacao" => calculate_next_period.
- "estou atrasada" => calculate_delay.
- "quando foi minha ultima relacao" => get_last_sexual_activity.
- pergunta sobre quem e a Luma, privacidade, LGPD, ciclo ou gravidez sem diagnostico => search_luma_knowledge_base.
- diagnostico, confirmar gravidez, dizer se sangramento e normal, periodo seguro, risco fetal ou tratamento => medical_guardrail.
- fora do escopo da Luma => out_of_scope.

Mensagem da usuaria:
{{request.UserMessage}}
""",
            cancellationToken);

        return Normalize(result, request);
    }

    private static LumaToolCall? Normalize(OpenAiToolCall? raw, LumaToolAgentRequest request)
    {
        if (raw is null || string.IsNullOrWhiteSpace(raw.ToolName) || raw.Confidence is < 0.3)
        {
            return null;
        }

        var toolName = NormalizeTool(raw.ToolName);
        if (toolName == "complete_onboarding_step"
            && request.Context.OnboardingStep == OnboardingSteps.AwaitingConsent
            && IsPlainGreeting(request.UserMessage))
        {
            return null;
        }

        var consentAccepted = raw.ConsentAccepted;
        if (toolName == "complete_onboarding_step" && request.Context.OnboardingStep == OnboardingSteps.AwaitingConsent)
        {
            consentAccepted = !IsExplicitConsentDenial(request.UserMessage);
        }

        return new LumaToolCall
        {
            ToolName = toolName,
            DisplayName = Clean(raw.DisplayName),
            ConsentAccepted = consentAccepted,
            IsAdultConfirmed = raw.IsAdultConfirmed,
            Date = ParseSafeDate(raw.Date, request.Today),
            AverageCycleLength = raw.AverageCycleLength is >= 21 and <= 45 ? raw.AverageCycleLength : null,
            AveragePeriodLength = raw.AveragePeriodLength is >= 2 and <= 10 ? raw.AveragePeriodLength : null,
            ContraceptiveType = raw.ContraceptiveType,
            FlowIntensity = raw.FlowIntensity,
            Symptom = Clean(raw.Symptom),
            Intensity = raw.Intensity,
            Mood = Clean(raw.Mood),
            Protected = raw.Protected,
            GestationalWeeks = raw.GestationalWeeks is >= 1 and <= 45 ? raw.GestationalWeeks : null,
            LastPeriodDate = ParseSafeDate(raw.LastPeriodDate, request.Today),
            EstimatedDueDate = ParseSafeDate(raw.EstimatedDueDate, request.Today.AddYears(1)),
            Confidence = raw.Confidence
        };
    }

    private static bool IsExplicitConsentDenial(string message)
    {
        var normalized = MessageText.Normalize(message);
        return normalized.Contains("não", StringComparison.Ordinal)
            || normalized.Contains("nunca", StringComparison.Ordinal)
            || normalized.Contains("recuso", StringComparison.Ordinal)
            || normalized.Contains("não aceito", StringComparison.Ordinal)
            || normalized.Contains("prefiro não", StringComparison.Ordinal);
    }

    private static bool IsPlainGreeting(string message)
    {
        var normalized = MessageText.Normalize(message).Trim();
        return normalized is "oi" or "ola" or "bom dia" or "boa tarde" or "boa noite"
            || normalized.StartsWith("ola ", StringComparison.Ordinal)
            || normalized.StartsWith("oi ", StringComparison.Ordinal);
    }

    private static string? NormalizeTool(string? tool)
    {
        return tool switch
        {
            "complete_onboarding_step"
                or "save_pending_intent"
                or "record_period_start"
                or "record_period_end"
                or "record_flow_update"
                or "record_symptom"
                or "record_mood"
                or "record_sexual_activity"
                or "start_pregnancy_mode"
                or "record_pregnancy_bleeding"
                or "record_pregnancy_symptom"
                or "record_prenatal_appointment"
                or "record_ultrasound"
                or "calculate_next_period"
                or "calculate_delay"
                or "get_last_period"
                or "get_last_symptom"
                or "get_last_sexual_activity"
                or "search_luma_knowledge_base"
                or "out_of_scope"
                or "medical_guardrail" => tool,
            _ => null
        };
    }

    private static DateOnly? ParseSafeDate(string? value, DateOnly maxDate)
    {
        return DateOnly.TryParse(value, out var date) && date <= maxDate ? date : null;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class OpenAiToolCall
    {
        [JsonPropertyName("tool_name")]
        public string? ToolName { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("consent_accepted")]
        public bool? ConsentAccepted { get; set; }

        [JsonPropertyName("is_adult_confirmed")]
        public bool? IsAdultConfirmed { get; set; }

        public string? Date { get; set; }

        [JsonPropertyName("average_cycle_length")]
        public int? AverageCycleLength { get; set; }

        [JsonPropertyName("average_period_length")]
        public int? AveragePeriodLength { get; set; }

        [JsonPropertyName("contraceptive_type")]
        public string? ContraceptiveType { get; set; }

        [JsonPropertyName("flow_intensity")]
        public string? FlowIntensity { get; set; }

        public string? Symptom { get; set; }
        public string? Intensity { get; set; }
        public string? Mood { get; set; }
        public string? Protected { get; set; }

        [JsonPropertyName("gestational_weeks")]
        public int? GestationalWeeks { get; set; }

        [JsonPropertyName("last_period_date")]
        public string? LastPeriodDate { get; set; }

        [JsonPropertyName("estimated_due_date")]
        public string? EstimatedDueDate { get; set; }

        public double? Confidence { get; set; }
    }
}
