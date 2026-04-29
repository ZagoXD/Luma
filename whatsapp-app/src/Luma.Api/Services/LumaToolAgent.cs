using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luma.Api.Models;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public interface ILumaToolAgent
{
    Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default);
}

public sealed record LumaToolAgentRequest(
    string UserMessage,
    DateOnly Today,
    ConversationContext Context,
    string? Knowledge,
    IReadOnlyList<string> AvailableTools);

public sealed class NullLumaToolAgent : ILumaToolAgent
{
    public Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LumaToolCall?>(null);
    }
}

public sealed class LumaToolCall
{
    public string? ToolName { get; set; }
    public string? DisplayName { get; set; }
    public bool? ConsentAccepted { get; set; }
    public bool? IsAdultConfirmed { get; set; }
    public DateOnly? Date { get; set; }
    public int? AverageCycleLength { get; set; }
    public int? AveragePeriodLength { get; set; }
    public string? ContraceptiveType { get; set; }
    public string? FlowIntensity { get; set; }
    public string? Symptom { get; set; }
    public string? Intensity { get; set; }
    public string? Mood { get; set; }
    public string? Protected { get; set; }
    public int? GestationalWeeks { get; set; }
    public DateOnly? LastPeriodDate { get; set; }
    public DateOnly? EstimatedDueDate { get; set; }
    public double? Confidence { get; set; }
}

public sealed class OllamaLumaToolAgent(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLumaToolAgent> logger) : ILumaToolAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OllamaOptions _options = options.Value;

    public async Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, _options.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var prompt = $$"""
Você e a Luma agente. Escolha UMA tool. Responda so JSON.
Hoje={{request.Today:yyyy-MM-dd}}
Etapa={{request.Context.OnboardingStep}}; CadastroCompleto={{request.Context.HasCompletedOnboarding}}; Pendencia={{request.Context.PendingAction ?? "nenhuma"}}
Mensagem={{request.UserMessage}}

Tools: complete_onboarding_step, record_period_start, record_period_end, record_flow_update, record_symptom, record_mood, record_sexual_activity, start_pregnancy_mode, record_pregnancy_bleeding, record_pregnancy_symptom, record_prenatal_appointment, record_ultrasound, calculate_next_period, calculate_delay, get_last_period, get_last_symptom, get_last_sexual_activity, search_luma_knowledge_base, out_of_scope, medical_guardrail.

Regras rapidas:
- awaiting_consent + concordancia ("claro", "pode seguir", "perfeitamente", "sim", "aceito") => complete_onboarding_step, consent_accepted=true.
- awaiting_display_name/age/date/cycle/period_length/contraceptive => complete_onboarding_step com campos encontrados.
- menstruacao iniciou => record_period_start. menstruacao terminou => record_period_end.
- relacao/sexo/intimidade sexual => record_sexual_activity.
- diagnostico/gravidez?/sangramento normal?/periodo seguro? => medical_guardrail.

Exemplos:
"perfeitamente, pode seguir" em awaiting_consent => {"tool_name":"complete_onboarding_step","consent_accepted":true,"confidence":0.9}
"Pode me chamar de Nay, tenho 21 anos" => {"tool_name":"complete_onboarding_step","display_name":"Nay","is_adult_confirmed":true,"confidence":0.9}
"menstruei hoje" => {"tool_name":"record_period_start","date":"{{request.Today:yyyy-MM-dd}}","confidence":0.9}

Formato:
{
  "tool_name": "complete_onboarding_step"|"save_pending_intent"|"record_period_start"|"record_period_end"|"record_flow_update"|"record_symptom"|"record_mood"|"record_sexual_activity"|"start_pregnancy_mode"|"record_pregnancy_bleeding"|"record_pregnancy_symptom"|"record_prenatal_appointment"|"record_ultrasound"|"calculate_next_period"|"calculate_delay"|"get_last_period"|"get_last_symptom"|"get_last_sexual_activity"|"search_luma_knowledge_base"|"out_of_scope"|"medical_guardrail"|null,
  "display_name": string|null,
  "consent_accepted": boolean|null,
  "is_adult_confirmed": boolean|null,
  "date": "yyyy-MM-dd"|null,
  "average_cycle_length": number|null,
  "average_period_length": number|null,
  "contraceptive_type": "pill"|"injection"|"hormonal_iud"|"copper_iud"|"implant"|"condom"|"none"|"other"|"prefer_not_say"|null,
  "flow_intensity": "light"|"medium"|"intense"|"unknown"|null,
  "symptom": string|null,
  "intensity": "light"|"moderate"|"strong"|null,
  "mood": string|null,
  "protected": "yes"|"no"|"unknown"|"prefer_not_say"|null,
  "gestational_weeks": number|null,
  "last_period_date": "yyyy-MM-dd"|null,
  "estimated_due_date": "yyyy-MM-dd"|null,
  "confidence": number
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
                    options = new { temperature = 0 }
                },
                SerializerOptions,
                linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama tool agent failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(SerializerOptions, linked.Token);
            if (string.IsNullOrWhiteSpace(payload?.Response))
            {
                return null;
            }

            var raw = JsonSerializer.Deserialize<OllamaToolCall>(payload.Response, SerializerOptions);
            return Normalize(raw, request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Ollama tool agent unavailable; using deterministic fallback.");
            return null;
        }
    }

    private static LumaToolCall? Normalize(OllamaToolCall? raw, LumaToolAgentRequest request)
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
        return normalized.Contains("nao", StringComparison.Ordinal)
            || normalized.Contains("nunca", StringComparison.Ordinal)
            || normalized.Contains("recuso", StringComparison.Ordinal)
            || normalized.Contains("nao aceito", StringComparison.Ordinal)
            || normalized.Contains("prefiro nao", StringComparison.Ordinal);
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

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }

    private sealed class OllamaToolCall
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
