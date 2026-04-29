using System.Text.Json.Serialization;

namespace Luma.Api.Services;

public sealed class OpenAiConversationIntentExtractor(OpenAiResponsesClient openAi) : IConversationIntentExtractor
{
    public async Task<ConversationIntent?> ExtractAsync(
        string message,
        DateOnly today,
        ConversationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var result = await openAi.CreateStructuredAsync<OpenAiConversationIntent>(
            "luma_conversation_intent",
            OpenAiJsonSchemas.ConversationIntent,
            """
Você e o extrator de intencoes da Luma, uma assistente de ciclo menstrual e gravidez pelo WhatsApp.
Extraia apenas a intencao e entidades explicitamente informadas. Não diagnostique, não invente e não responda em linguagem natural.
""",
            $$"""
Hoje: {{today:yyyy-MM-dd}}
Contexto:
- nome conhecido: {{context?.DisplayName ?? "desconhecido"}}
- etapa: {{context?.OnboardingStep ?? "desconhecida"}}
- cadastro completo: {{(context?.HasCompletedOnboarding == true ? "sim" : "não")}}
- consentimento aceito: {{(context?.HasAcceptedConsent == true ? "sim" : "não")}}
- pendencia: {{context?.PendingAction ?? "nenhuma"}}

Regras:
- period_start: inicio da menstruacao.
- period_end: fim da menstruacao.
- flow_update, symptom ou mood: registros de fluxo, sintomas ou humor.
- sexual_activity: qualquer relato de relacao sexual, sexo ou intimidade sexual.
- last_sexual_activity_question: pergunta sobre a ultima relacao sexual.
- pregnancy_positive: usuaria informa gravidez ou teste positivo.
- pregnancy_bleeding: sangramento durante gravidez.
- pregnancy_symptom: sintomas de gravidez.
- prenatal_appointment: consulta de pre-natal/obstetra.
- ultrasound: ultrassom.
- luma_identity_question: "quem e você", "o que você faz".
- knowledge_question: privacidade, LGPD, limites da Luma, ciclo ou gravidez sem diagnostico.
- out_of_scope: fora de ciclo menstrual, gravidez, sintomas, registros, privacidade ou funcionamento da Luma.
- Datas relativas: hoje={{today:yyyy-MM-dd}}, ontem={{today.AddDays(-1):yyyy-MM-dd}}.
- Se houver duvida, intent=null e confidence baixo.

Mensagem:
{{message}}
""",
            cancellationToken);

        return Normalize(result, today);
    }

    private static ConversationIntent? Normalize(OpenAiConversationIntent? ai, DateOnly today)
    {
        if (ai is null || NormalizeIntent(ai.Intent) is not { } intent)
        {
            return null;
        }

        var result = new ConversationIntent
        {
            Intent = intent,
            GestationalWeeks = ai.GestationalWeeks is >= 1 and <= 45 ? ai.GestationalWeeks : null,
            Protected = ai.Protected is "yes" or "no" or "unknown" or "prefer_not_say" ? ai.Protected : null,
            Symptom = string.IsNullOrWhiteSpace(ai.Symptom) ? null : ai.Symptom.Trim(),
            Intensity = ai.Intensity is "light" or "moderate" or "strong" ? ai.Intensity : null,
            Confidence = ai.Confidence
        };

        if (DateOnly.TryParse(ai.Date, out var date) && date <= today.AddDays(1))
        {
            result.Date = date;
        }

        if (DateOnly.TryParse(ai.LastPeriodDate, out var lastPeriod) && lastPeriod <= today.AddDays(1))
        {
            result.LastPeriodDate = lastPeriod;
        }

        if (DateOnly.TryParse(ai.EstimatedDueDate, out var dueDate))
        {
            result.EstimatedDueDate = dueDate;
        }

        return result;
    }

    private static string? NormalizeIntent(string? intent)
    {
        return intent switch
        {
            ConversationIntents.PeriodStart
                or ConversationIntents.PeriodEnd
                or ConversationIntents.FlowUpdate
                or ConversationIntents.Symptom
                or ConversationIntents.Mood
                or ConversationIntents.SexualActivity
                or ConversationIntents.LastSexualActivityQuestion
                or ConversationIntents.PregnancyPositive
                or ConversationIntents.PregnancyBleeding
                or ConversationIntents.PregnancySymptom
                or ConversationIntents.PrenatalAppointment
                or ConversationIntents.Ultrasound
                or ConversationIntents.PregnancyWeeksQuestion
                or ConversationIntents.PregnancyDueDateQuestion
                or ConversationIntents.LumaIdentityQuestion
                or ConversationIntents.KnowledgeQuestion
                or ConversationIntents.OutOfScope => intent,
            _ => null
        };
    }

    private sealed class OpenAiConversationIntent
    {
        public string? Intent { get; set; }
        public string? Date { get; set; }

        [JsonPropertyName("gestational_weeks")]
        public int? GestationalWeeks { get; set; }

        [JsonPropertyName("last_period_date")]
        public string? LastPeriodDate { get; set; }

        [JsonPropertyName("estimated_due_date")]
        public string? EstimatedDueDate { get; set; }

        public string? Protected { get; set; }
        public string? Symptom { get; set; }
        public string? Intensity { get; set; }
        public double? Confidence { get; set; }
    }
}
