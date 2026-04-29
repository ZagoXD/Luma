namespace Luma.Api.Services;

public static class LumaKnowledgeBase
{
    public static string? Search(string normalizedMessage)
    {
        var matches = Entries
            .Where(entry => entry.Keywords.Any(keyword => normalizedMessage.Contains(keyword, StringComparison.Ordinal)))
            .Take(3)
            .Select(entry => $"{entry.Title}: {entry.Content}")
            .ToList();

        return matches.Count == 0 ? null : string.Join("\n", matches);
    }

    public static bool IsKnowledgeQuestion(string normalizedMessage)
    {
        return IsPrivacyQuestion(normalizedMessage)
            || normalizedMessage.Contains("consentimento", StringComparison.Ordinal)
            || normalizedMessage.Contains("lgpd", StringComparison.Ordinal);
    }

    private static bool IsPrivacyQuestion(string normalizedMessage)
    {
        return normalizedMessage.Contains("privacidade", StringComparison.Ordinal)
            || normalizedMessage.Contains("protege meus dados", StringComparison.Ordinal)
            || normalizedMessage.Contains("meus dados", StringComparison.Ordinal)
            || normalizedMessage.Contains("dados ficam", StringComparison.Ordinal);
    }

    private static readonly IReadOnlyList<KnowledgeEntry> Entries =
    [
        new(
            "Privacidade e LGPD",
            ["privacidade", "protege meus dados", "meus dados", "dados ficam", "lgpd", "consentimento"],
            "A Luma usa dados de ciclo, sintomas, relacoes e gravidez apenas para manter historico e responder dentro desse contexto. Dados de saude sao sensiveis; o cadastro pede consentimento, o backend valida gravacoes e a usuaria pode pedir para apagar dados com confirmacao segura."),
        new(
            "Limites medicos",
            ["diagnostico", "gravida", "gravidez", "normal", "risco", "sangramento", "dor forte", "febre"],
            "A Luma não faz diagnosticos, não confirma gravidez, não diz se sangramento e normal e não substitui medico, ginecologista, obstetra ou pre-natal. Em sinais de alerta, deve orientar procurar atendimento profissional."),
        new(
            "Ciclo menstrual",
            ["menstruacao", "menstruei", "ciclo", "fluxo", "colica", "sintoma", "humor", "atrasada"],
            "Registros de menstruacao, fluxo, sintomas e humor servem para historico e estimativas. Previsoes de proxima menstruacao ou atraso sao calculadas com base nos dados informados e nunca sao certeza."),
        new(
            "Relacao sexual",
            ["relacao", "sexo", "transa", "camisinha", "protecao"],
            "Registros de relacao sexual ficam apenas no historico da usuaria. A Luma não usa isso para afirmar gravidez, periodo fertil seguro ou risco individual."),
        new(
            "Gravidez",
            ["pre natal", "prenatal", "obstetra", "ultrassom", "semanas", "dpp", "parto"],
            "Na gravidez, a Luma organiza registros como semanas estimadas, DPP, consultas, ultrassom, sintomas e sangramentos. Tudo e apoio de historico e deve ser confirmado no pre-natal.")
    ];

    private sealed record KnowledgeEntry(string Title, IReadOnlyList<string> Keywords, string Content);
}
