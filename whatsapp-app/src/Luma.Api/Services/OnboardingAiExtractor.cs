using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class OnboardingAiExtractor(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OnboardingAiExtractor> logger) : IOnboardingDataExtractor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OllamaOptions _options = options.Value;

    public async Task<OnboardingExtraction?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, _options.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var prompt = $$"""
Voce e um extrator de dados para onboarding de uma assistente menstrual chamada Luma.
Extraia apenas dados explicitamente informados pela usuaria. Nao invente.
Hoje e {{today:yyyy-MM-dd}}.

Responda somente JSON valido, sem markdown, com este formato:
{
  "display_name": string|null,
  "is_adult_confirmed": boolean|null,
  "last_period_start_date": "yyyy-MM-dd"|null,
  "last_period_days_ago": number|null,
  "last_period_unknown": boolean,
  "average_cycle_length": number|null,
  "average_period_length": number|null
}

Regras:
- display_name: primeiro nome ou apelido quando ela disser "meu nome e", "sou", "me chamo", "pode me chamar de".
- is_adult_confirmed: true quando disser que tem 18 anos ou mais, ou informar idade >= 18. false se idade < 18 ou negar.
- last_period_start_date: primeiro dia da ultima menstruacao quando houver uma data absoluta, como 10/04, dia 10 de abril, ou dia 30 do mes passado. Se a usuaria disser apenas "dia 10", interprete como a ocorrencia mais recente desse dia no calendario: se hoje e dia 25/04/2026, "dia 10" = 2026-04-10; se hoje e dia 08/04/2026, "dia 10" = 2026-03-10.
- last_period_days_ago: numero de dias atras quando a usuaria usar data relativa. Exemplos: hoje = 0, ontem = 1, anteontem/antes de ontem = 2, antes de antes de ontem = 3, "ha uns 5 dias" = 5, "fazem 3 dias" = 3.
- last_period_unknown: true quando disser que nao lembra ou nao sabe a ultima menstruacao.
- average_cycle_length: intervalo/duracao do ciclo em dias, geralmente entre 21 e 45.
- average_period_length: duracao da menstruacao em dias, geralmente entre 2 e 10.
- Se houver duvida, use null.

Mensagem da usuaria:
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
                    options = new
                    {
                        temperature = 0
                    }
                },
                SerializerOptions,
                linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama extraction failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(SerializerOptions, linked.Token);
            if (string.IsNullOrWhiteSpace(payload?.Response))
            {
                return null;
            }

            var ai = JsonSerializer.Deserialize<OllamaOnboardingExtraction>(payload.Response, SerializerOptions);
            return Normalize(ai, today);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Ollama extraction unavailable; falling back to deterministic onboarding.");
            return null;
        }
    }

    private static OnboardingExtraction? Normalize(OllamaOnboardingExtraction? ai, DateOnly today)
    {
        if (ai is null)
        {
            return null;
        }

        var extraction = new OnboardingExtraction
        {
            DisplayName = CleanName(ai.DisplayName),
            IsAdultConfirmed = ai.IsAdultConfirmed,
            LastPeriodUnknown = ai.LastPeriodUnknown,
            AverageCycleLength = ai.AverageCycleLength is >= 21 and <= 45 ? ai.AverageCycleLength : null,
            AveragePeriodLength = ai.AveragePeriodLength is >= 2 and <= 10 ? ai.AveragePeriodLength : null
        };

        if (ai.LastPeriodDaysAgo is >= 0 and <= 120)
        {
            extraction.LastPeriodDaysAgo = ai.LastPeriodDaysAgo;
            extraction.LastPeriodStartDate = today.AddDays(-ai.LastPeriodDaysAgo.Value);
        }

        if (DateOnly.TryParse(ai.LastPeriodStartDate, out var lastPeriod) && lastPeriod <= today.AddDays(1))
        {
            extraction.LastPeriodStartDate = lastPeriod;
        }

        return extraction.HasAnyValue() ? extraction : null;
    }

    private static string? CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length is < 2 or > 60)
        {
            return null;
        }

        return trimmed;
    }

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }

    private sealed class OllamaOnboardingExtraction
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("is_adult_confirmed")]
        public bool? IsAdultConfirmed { get; set; }

        [JsonPropertyName("last_period_start_date")]
        public string? LastPeriodStartDate { get; set; }

        [JsonPropertyName("last_period_days_ago")]
        public int? LastPeriodDaysAgo { get; set; }

        [JsonPropertyName("last_period_unknown")]
        public bool LastPeriodUnknown { get; set; }

        [JsonPropertyName("average_cycle_length")]
        public int? AverageCycleLength { get; set; }

        [JsonPropertyName("average_period_length")]
        public int? AveragePeriodLength { get; set; }
    }
}
