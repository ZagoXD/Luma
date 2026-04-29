using System.Text.Json.Serialization;

namespace Luma.Api.Services;

public sealed class OpenAiOnboardingDataExtractor(OpenAiResponsesClient openAi) : IOnboardingDataExtractor
{
    public async Task<OnboardingExtraction?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var result = await openAi.CreateStructuredAsync<OpenAiOnboardingExtraction>(
            "luma_onboarding_extraction",
            OpenAiJsonSchemas.OnboardingExtraction,
            """
Você é o extrator de dados do onboarding da Luma, uma assistente de ciclo menstrual e gravidez pelo WhatsApp.
Extraia apenas dados explicitamente informados pela usuaria. Não invente. Não responda como assistente; apenas preencha o schema.
""",
            $$"""
Hoje: {{today:yyyy-MM-dd}}

Regras:
- display_name: primeiro nome ou apelido quando ela disser "meu nome e", "sou", "me chamo", "pode me chamar de".
- is_adult_confirmed: true quando disser que tem 18 anos ou mais, ou informar idade >= 18. false se idade < 18 ou negar.
- last_period_start_date: primeiro dia da ultima menstruacao quando houver data absoluta. "dia 10" deve ser a ocorrencia mais recente desse dia no calendario.
- last_period_days_ago: hoje=0, ontem=1, anteontem/antes de ontem=2, antes de antes de ontem=3, "ha uns 5 dias"=5.
- last_period_unknown: true quando disser que não lembra ou não sabe.
- average_cycle_length: intervalo do ciclo em dias, geralmente 21 a 45.
- average_period_length: duracao da menstruacao em dias, geralmente 2 a 10.
- contraceptive_type: pilula=pill, injecao=injection, DIU hormonal=hormonal_iud, DIU de cobre=copper_iud, implante=implant, camisinha=condom, não uso=none, prefiro não informar=prefer_not_say.

Mensagem da usuaria:
{{message}}
""",
            cancellationToken);

        return Normalize(result, today);
    }

    private static OnboardingExtraction? Normalize(OpenAiOnboardingExtraction? ai, DateOnly today)
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
            AveragePeriodLength = ai.AveragePeriodLength is >= 2 and <= 10 ? ai.AveragePeriodLength : null,
            ContraceptiveType = NormalizeContraceptiveType(ai.ContraceptiveType)
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
        return trimmed.Length is < 2 or > 60 ? null : trimmed;
    }

    private static string? NormalizeContraceptiveType(string? value)
    {
        return value switch
        {
            "pill" or "injection" or "hormonal_iud" or "copper_iud" or "implant" or "condom" or "none" or "other" or "prefer_not_say" => value,
            _ => null
        };
    }

    private sealed class OpenAiOnboardingExtraction
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

        [JsonPropertyName("contraceptive_type")]
        public string? ContraceptiveType { get; set; }
    }
}
