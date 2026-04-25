namespace Luma.Api.Services;

public static class SafetyGuardrail
{
    public const string SafeReply = "Não consigo confirmar isso por aqui. Posso te ajudar a organizar seus registros, mas para diagnóstico ou decisão médica o ideal é procurar um profissional de saúde.";

    public static bool ShouldBlock(string normalizedBody)
    {
        if (normalizedBody.Contains("estou gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("estou grávida", StringComparison.Ordinal)
            || normalizedBody.Contains("to gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("tô grávida", StringComparison.Ordinal)
            || normalizedBody.Contains("posso estar gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("posso estar grávida", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedBody.Contains("e normal", StringComparison.Ordinal)
            || normalizedBody.Contains("é normal", StringComparison.Ordinal)
            || normalizedBody.Contains("infeccao", StringComparison.Ordinal)
            || normalizedBody.Contains("infecção", StringComparison.Ordinal)
            || normalizedBody.Contains("endometriose", StringComparison.Ordinal)
            || normalizedBody.Contains("sem protecao", StringComparison.Ordinal)
            || normalizedBody.Contains("sem proteção", StringComparison.Ordinal)
            || normalizedBody.Contains("periodo seguro", StringComparison.Ordinal)
            || normalizedBody.Contains("período seguro", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
