namespace Luma.Api.Services;

public static class LegalConsent
{
    public const string Version = "2026-11-22-v2";
    public static string? Validate(bool accepted, string? version)
        => accepted && version == Version ? null : "Leia e aceite a versão atual dos Termos de Uso e da Política de Privacidade para criar sua conta.";
}
