using System.Text.RegularExpressions;

namespace Luma.Api.Services;

public static partial class PortugueseReplyPolisher
{
    private static readonly IReadOnlyList<(string From, string To)> PhraseReplacements =
    [
        ("Como esta", "Como está"),
        ("como esta", "como está"),
        ("esta prevista", "está prevista"),
        ("esta cerca", "está cerca"),
        ("esta registrado", "está registrado"),
        ("esta atrasada", "está atrasada"),
        ("esta se sentindo", "está se sentindo"),
        ("voce esta", "você está"),
        ("Voce esta", "Você está"),
        ("Isso e so", "Isso é só"),
        ("isso e so", "isso é só"),
        ("Essa e uma", "Essa é uma"),
        ("essa e uma", "essa é uma"),
        ("O ideal e", "O ideal é"),
        ("o ideal e", "o ideal é"),
        ("Meu intuito e", "Meu intuito é"),
        ("quando e minha", "quando é minha"),
        ("Quando e minha", "Quando é minha"),
        ("Estou por aqui", "Estou por aqui"),
        ("Data de termino", "Data de término"),
        ("data de termino", "data de término")
    ];

    private static readonly IReadOnlyDictionary<string, string> WordReplacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Não"] = "Não",
        ["não"] = "não",
        ["Você"] = "Você",
        ["você"] = "você",
        ["saude"] = "saúde",
        ["Saude"] = "Saúde",
        ["diagnostico"] = "diagnóstico",
        ["Diagnostico"] = "Diagnóstico",
        ["diagnosticos"] = "diagnósticos",
        ["Diagnosticos"] = "Diagnósticos",
        ["orientacao"] = "orientação",
        ["Orientacao"] = "Orientação",
        ["medica"] = "médica",
        ["Medica"] = "Médica",
        ["medicas"] = "médicas",
        ["Medicas"] = "Médicas",
        ["medico"] = "médico",
        ["Medico"] = "Médico",
        ["menstruacao"] = "menstruação",
        ["Menstruacao"] = "Menstruação",
        ["relacao"] = "relação",
        ["Relacao"] = "Relação",
        ["relacoes"] = "relações",
        ["Relacoes"] = "Relações",
        ["historico"] = "histórico",
        ["Historico"] = "Histórico",
        ["proxima"] = "próxima",
        ["Proxima"] = "Próxima",
        ["ultima"] = "última",
        ["Ultima"] = "Última",
        ["colica"] = "cólica",
        ["Colica"] = "Cólica",
        ["tambem"] = "também",
        ["Tambem"] = "Também",
        ["seguranca"] = "segurança",
        ["Seguranca"] = "Segurança",
        ["inicio"] = "início",
        ["Inicio"] = "Início",
        ["termino"] = "término",
        ["Termino"] = "Término",
        ["media"] = "média",
        ["Media"] = "Média",
        ["previsao"] = "previsão",
        ["Previsao"] = "Previsão",
        ["decisoes"] = "decisões",
        ["Decisoes"] = "Decisões",
        ["varios"] = "vários",
        ["Varios"] = "Vários",
        ["variacao"] = "variação",
        ["Variacao"] = "Variação",
        ["alteracoes"] = "alterações",
        ["Alteracoes"] = "Alterações",
        ["padroes"] = "padrões",
        ["Padroes"] = "Padrões",
        ["gravida"] = "grávida",
        ["Gravida"] = "Grávida",
        ["duvidas"] = "dúvidas",
        ["Duvidas"] = "Dúvidas",
        ["so"] = "só",
        ["So"] = "Só",
        ["ja"] = "já",
        ["Ja"] = "Já",
        ["comecar"] = "começar",
        ["Comecar"] = "Começar",
        ["comecou"] = "começou",
        ["Comecou"] = "Começou",
        ["faco"] = "faço",
        ["Faco"] = "Faço",
        ["opcoes"] = "opções",
        ["Opcoes"] = "Opções",
        ["opcao"] = "opção",
        ["Opcao"] = "Opção",
        ["confirmacao"] = "confirmação",
        ["Confirmacao"] = "Confirmação",
        ["informacao"] = "informação",
        ["Informacao"] = "Informação",
        ["informacoes"] = "informações",
        ["Informacoes"] = "Informações",
        ["duracao"] = "duração",
        ["Duracao"] = "Duração",
        ["atencao"] = "atenção",
        ["Atencao"] = "Atenção",
        ["sensivel"] = "sensível",
        ["sensiveis"] = "sensíveis",
        ["sensibilidade"] = "sensibilidade",
        ["liquido"] = "líquido",
        ["pre"] = "pré",
        ["fertil"] = "fértil",
        ["proteção"] = "proteção",
        ["protecao"] = "proteção",
        ["Pilula"] = "Pílula",
        ["pilula"] = "pílula",
        ["Injecao"] = "Injeção",
        ["injecao"] = "injeção",
        ["Medio"] = "Médio",
        ["medio"] = "médio"
    };

    public static string Apply(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return reply;
        }

        var polished = reply;
        foreach (var (from, to) in PhraseReplacements)
        {
            polished = polished.Replace(from, to, StringComparison.Ordinal);
        }

        return WordRegex().Replace(polished, match =>
            WordReplacements.TryGetValue(match.Value, out var replacement)
                ? replacement
                : match.Value);
    }

    [GeneratedRegex(@"\b[\p{L}]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
