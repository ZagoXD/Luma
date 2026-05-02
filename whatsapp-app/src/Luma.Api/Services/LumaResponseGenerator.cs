namespace Luma.Api.Services;

public interface ILumaResponseGenerator
{
    Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default);
}

public sealed record LumaResponseRequest(
    string UserMessage,
    string BackendResult,
    string OnboardingStep,
    string? DisplayName,
    string? PendingAction,
    bool IsGuardrail,
    IReadOnlyList<string> AvailableTools,
    string? Knowledge);

public sealed class PassthroughLumaResponseGenerator : ILumaResponseGenerator
{
    public Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.BackendResult);
    }
}

public static class LumaTools
{
    public static readonly IReadOnlyList<string> Available =
    [
        "get_user_profile: le nome, preferencias e dados basicos autorizados da usuaria",
        "get_onboarding_state: le etapa atual do cadastro e pendencias",
        "save_pending_intent: guarda intencao fora de ordem para confirmar depois",
        "complete_onboarding_step: avanca uma etapa validada do cadastro",
        "record_period_start: registra inicio de menstruacao validado pelo backend",
        "record_period_end: registra termino de menstruacao validado pelo backend",
        "record_flow_update: registra fluxo leve, medio, intenso ou nao informado",
        "record_symptom: registra sintomas do ciclo sem diagnosticar",
        "record_mood: registra humor para historico",
        "record_sexual_activity: registra relacao sexual para historico",
        "start_pregnancy_mode: inicia acompanhamento de gravidez informado pela usuaria",
        "record_pregnancy_bleeding: registra sangramento na gravidez e aciona orientacao segura",
        "record_pregnancy_symptom: registra sintoma de gravidez",
        "record_prenatal_appointment: registra consulta de pre-natal ou obstetra",
        "record_ultrasound: registra ultrassom",
        "get_baby_development: consulta tamanho e desenvolvimento fetal por semana gestacional",
        "generate_baby_size_image: gera uma imagem educativa do tamanho aproximado do bebe e retorna link/imagem",
        "get_cycle_calendar: gera calendario mensal visual com menstruacao, previsoes, relacoes, janela fertil e gravidez",
        "calculate_next_period: calcula estimativa de proxima menstruacao",
        "calculate_delay: calcula estimativa de atraso menstrual",
        "get_last_period: consulta ultima menstruacao registrada",
        "get_last_symptom: consulta ultimo sintoma registrado",
        "get_last_sexual_activity: consulta ultima relacao sexual registrada",
        "get_notification_preferences: consulta preferencias de notificacao da usuaria",
        "update_notification_preferences: ativa/atualiza lembretes de menstruacao, anticoncepcional, check-in e horario",
        "disable_notification_preferences: desativa lembretes automaticos",
        "search_luma_knowledge_base: busca trechos RAG seguros sobre Luma, ciclo, gravidez, privacidade e limites medicos"
    ];
}
