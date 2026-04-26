using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class OllamaConversationIntentExtractor(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaConversationIntentExtractor> logger) : IConversationIntentExtractor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OllamaOptions _options = options.Value;

    public async Task<ConversationIntent?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, _options.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var prompt = $$"""
Voce e o extrator de intencoes da Luma, uma assistente de ciclo menstrual e gravidez pelo WhatsApp.
Extraia apenas a intencao e entidades explicitamente informadas. Nao diagnostique, nao invente.
Hoje e {{today:yyyy-MM-dd}}.

Responda somente JSON valido, sem markdown:
{
  "intent": "sexual_activity"|"last_sexual_activity_question"|"pregnancy_positive"|"pregnancy_bleeding"|"pregnancy_symptom"|"prenatal_appointment"|"ultrasound"|"pregnancy_weeks_question"|"pregnancy_due_date_question"|"luma_identity_question"|"out_of_scope"|null,
  "date": "yyyy-MM-dd"|null,
  "gestational_weeks": number|null,
  "last_period_date": "yyyy-MM-dd"|null,
  "estimated_due_date": "yyyy-MM-dd"|null,
  "protected": "yes"|"no"|"unknown"|"prefer_not_say"|null,
  "symptom": string|null,
  "intensity": "light"|"moderate"|"strong"|null,
  "confidence": number
}

Regras:
- Use sexual_activity para qualquer mensagem que informe relacao sexual, sexo, intimidade sexual ou equivalente, mesmo com linguagem informal.
- Use last_sexual_activity_question para perguntas sobre quando foi a ultima relacao sexual.
- Use pregnancy_positive para mensagens informando gravidez ou teste positivo. Se houver "8 semanas", preencha gestational_weeks.
- Use pregnancy_bleeding para sangramento durante gravidez.
- Use pregnancy_symptom para sintomas de gravidez como nausea, cansaco, azia, tontura, colica, dor.
- Use prenatal_appointment para consulta de pre-natal/obstetra.
- Use ultrasound para ultrassom.
- Use luma_identity_question para "quem e voce", "o que voce faz", "o que e a Luma".
- Use out_of_scope para perguntas fora de ciclo menstrual, gravidez, sintomas, registros, privacidade ou funcionamento da Luma.
- Datas relativas: hoje={{today:yyyy-MM-dd}}, ontem={{today.AddDays(-1):yyyy-MM-dd}}.
- Se houver duvida, use null e confidence baixo.

Mensagem:
{{message}}
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
                    options = new { temperature = 0 }
                },
                SerializerOptions,
                linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama conversation intent failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(SerializerOptions, linked.Token);
            if (string.IsNullOrWhiteSpace(payload?.Response))
            {
                return null;
            }

            var ai = JsonSerializer.Deserialize<OllamaConversationIntent>(payload.Response, SerializerOptions);
            return Normalize(ai, today);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Ollama conversation intent unavailable; falling back to deterministic parser.");
            return null;
        }
    }

    private static ConversationIntent? Normalize(OllamaConversationIntent? ai, DateOnly today)
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
            ConversationIntents.SexualActivity
                or ConversationIntents.LastSexualActivityQuestion
                or ConversationIntents.PregnancyPositive
                or ConversationIntents.PregnancyBleeding
                or ConversationIntents.PregnancySymptom
                or ConversationIntents.PrenatalAppointment
                or ConversationIntents.Ultrasound
                or ConversationIntents.PregnancyWeeksQuestion
                or ConversationIntents.PregnancyDueDateQuestion
                or ConversationIntents.LumaIdentityQuestion
                or ConversationIntents.OutOfScope => intent,
            _ => null
        };
    }

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }

    private sealed class OllamaConversationIntent
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
