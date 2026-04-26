namespace Luma.Api.Services;

public static class SafetyGuardrail
{
    public const string SafeReply = "Nao consigo confirmar isso por aqui. Posso te ajudar a organizar seus registros, mas para diagnostico, gravidez, sangramentos ou decisoes medicas o ideal e procurar um profissional de saude.";

    public static bool ShouldBlock(string normalizedBody)
    {
        if (normalizedBody.Contains("estou gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("to gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("tou gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("posso estar gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("sera que estou gravida", StringComparison.Ordinal)
            || normalizedBody.Contains("teste deu positivo", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedBody.Contains("e normal", StringComparison.Ordinal)
            || normalizedBody.Contains("infeccao", StringComparison.Ordinal)
            || normalizedBody.Contains("endometriose", StringComparison.Ordinal)
            || normalizedBody.Contains("periodo seguro", StringComparison.Ordinal)
            || normalizedBody.Contains("nao preciso procurar medico", StringComparison.Ordinal)
            || normalizedBody.Contains("nao precisa procurar medico", StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedBody.Contains("sem protecao", StringComparison.Ordinal)
            && (normalizedBody.Contains("posso", StringComparison.Ordinal)
                || normalizedBody.Contains("pode", StringComparison.Ordinal)
                || normalizedBody.Contains("seguro", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }
}
