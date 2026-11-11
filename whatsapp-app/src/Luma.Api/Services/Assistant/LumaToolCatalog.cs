using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Luma.Api.Services.Assistant;

public sealed record KnowledgeSource(string Id, string Title, string Url, string Version, string? Attribution = null, string? LicenseUrl = null);
public sealed record ToolOutcome(bool Success, string Message, bool Fixed = false,
    string? MediaUrl = null, IReadOnlyList<KnowledgeSource>? Sources = null, string? State = null, bool ContextOnly = false);

public static class LumaToolCatalog
{
    private static readonly Dictionary<string, (string Description, string[] Fields, string[] Required)> Definitions = new()
    {
        ["get_user_profile"] = ("Consulta dados pessoais autorizados e preferências menstruais reais.", [], []),
        ["get_onboarding_state"] = ("Consulta a etapa atual, o próximo dado necessário e intenções aguardando confirmação.", [], []),
        ["complete_onboarding_step"] = ("Registra somente dados explicitamente informados no cadastro; não infira idade ou consentimento a partir do nome. Se a usuária não lembra a DUM, use last_period_unknown=true.", ["display_name", "consent_accepted", "is_adult_confirmed", "date", "last_period_unknown", "average_cycle_length", "average_period_length", "contraceptive_type"], []),
        ["confirm_pending_intent"] = ("Confirma ou descarta a intenção pendente após cadastro concluído, só mediante resposta explícita da usuária.", ["confirmed"], ["confirmed"]),
        ["record_period_start"] = ("Registra início real da menstruação. Fora da ordem, guarda uma intenção para confirmação após o cadastro.", ["date", "flow_intensity"], ["date"]),
        ["record_period_end"] = ("Registra término real; duração habitual não significa que terminou.", ["date"], ["date"]),
        ["record_flow_update"] = ("Registra a intensidade do fluxo.", ["date", "flow_intensity"], ["date", "flow_intensity"]),
        ["record_symptom"] = ("Registra sintoma menstrual sem diagnóstico.", ["date", "symptom", "intensity"], ["date", "symptom"]),
        ["record_mood"] = ("Registra humor.", ["date", "mood"], ["date", "mood"]),
        ["record_sexual_activity"] = ("Registra relação sexual informada, com data e proteção opcional.", ["date", "protected"], ["date"]),
        ["start_pregnancy_mode"] = ("Inicia acompanhamento da gravidez relatada; semanas, DUM ou DPP são referências informadas, não diagnóstico.", ["date", "gestational_weeks", "last_period_date", "estimated_due_date"], []),
        ["record_pregnancy_bleeding"] = ("Registra sangramento relatado durante gravidez e retorna encaminhamento fixo de segurança.", ["date"], ["date"]),
        ["record_pregnancy_symptom"] = ("Registra sintoma durante gravidez.", ["date", "symptom", "intensity"], ["date", "symptom"]),
        ["record_prenatal_appointment"] = ("Registra consulta de pré-natal agendada ou realizada.", ["date"], ["date"]),
        ["record_ultrasound"] = ("Registra data do ultrassom.", ["date"], ["date"]),
        ["get_baby_development"] = ("Consulta estimativa educativa de tamanho e desenvolvimento pela semana gestacional.", ["baby_development_week"], []),
        ["generate_baby_size_image"] = ("Solicita imagem educativa do bebê, disponível no Essencial; geração assíncrona.", ["baby_development_week"], []),
        ["get_cycle_calendar"] = ("Obtém link autenticado do calendário no mês pedido, sem criar novos registros.", ["calendar_month"], ["calendar_month"]),
        ["calculate_next_period"] = ("Calcula previsão pessoal da próxima menstruação.", [], []),
        ["calculate_delay"] = ("Calcula atraso estimado com os registros reais.", [], []),
        ["get_last_period"] = ("Consulta a última menstruação registrada.", [], []),
        ["get_last_symptom"] = ("Consulta o último sintoma registrado.", [], []),
        ["get_last_sexual_activity"] = ("Consulta a última relação sexual registrada.", [], []),
        ["get_pregnancy_due_date"] = ("Calcula a DPP estimada com a gravidez registrada.", [], []),
        ["get_pregnancy_weeks"] = ("Consulta semanas estimadas da gravidez registrada.", [], []),
        ["get_fertile_window"] = ("Consulta janela fértil e ovulação estimadas sem prometer contracepção.", [], []),
        ["update_user_preferences"] = ("Atualiza duração habitual do ciclo/menstruação ou método; não cria início nem término menstrual.", ["average_cycle_length", "average_period_length", "contraceptive_type"], []),
        ["get_notification_preferences"] = ("Consulta lembretes e horários salvos.", [], []),
        ["update_notification_preferences"] = ("Atualiza os lembretes explicitamente solicitados; horários individuais HH:mm; plano Essencial obrigatório.", ["period_reminder_enabled", "contraceptive_reminder_enabled", "symptom_checkin_enabled", "reminder_time", "period_reminder_time", "contraceptive_reminder_time", "symptom_checkin_time", "timezone", "contraceptive_type"], []),
        ["disable_notification_preferences"] = ("Desativa todos os lembretes, mediante solicitação.", [], []),
        ["search_luma_knowledge_base"] = ("Busca conhecimento revisado com fontes sobre ciclo, gravidez, privacidade e produto. Não consulta histórico pessoal.", ["query"], ["query"]),
        ["medical_guardrail"] = ("Retorna orientação fixa para diagnóstico, tratamento individual ou avaliação de sangramento.", [], []),
        ["out_of_scope"] = ("Recusa assuntos fora do acompanhamento menstrual/gestação e produto.", [], []),
        ["ask_clarification"] = ("Pede esclarecimento sem afirmar qualquer gravação.", ["question"], ["question"])
    };

    public static IReadOnlyList<Tool> Tools { get; } = Definitions.Select(pair =>
    {
        var baseSchema = JsonSerializer.SerializeToNode(OpenAiJsonSchemas.ToolCall)!["properties"]!;
        var properties = new JsonObject();
        foreach (var field in pair.Value.Fields)
        {
            properties[field] = baseSchema[field]?.DeepClone() ?? new JsonObject { ["type"] = field is "confirmed" or "last_period_unknown" ? "boolean" : "string" };
        }
        return new Tool
        {
            Name = pair.Key, Description = pair.Value.Description,
            InputSchema = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["type"] = "object", ["properties"] = properties,
                ["required"] = new JsonArray(pair.Value.Required.Select(value => JsonValue.Create(value)).ToArray()),
                ["additionalProperties"] = false
            })
        };
    }).ToArray();

    public static string? Validate(string name, JsonElement arguments)
    {
        if (!Definitions.TryGetValue(name, out var definition) || arguments.ValueKind != JsonValueKind.Object)
            return "Ferramenta ou argumentos inválidos.";
        if (arguments.EnumerateObject().Any(field => !definition.Fields.Contains(field.Name))
            || definition.Required.Any(field => !arguments.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null))
            return "Informe os campos obrigatórios, sem argumentos adicionais.";
        if (name is "update_user_preferences" or "complete_onboarding_step" or "update_notification_preferences"
            && !arguments.EnumerateObject().Any(field => field.Value.ValueKind != JsonValueKind.Null))
            return "Informe pelo menos um dado explicitamente fornecido pela usuária.";
        foreach (var field in arguments.EnumerateObject())
        {
            var value = field.Value;
            if (value.ValueKind == JsonValueKind.Null) continue;
            if (field.Name is "confirmed" or "consent_accepted" or "is_adult_confirmed" or "last_period_unknown"
                || field.Name.EndsWith("_enabled", StringComparison.Ordinal))
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "Valor booleano inválido.";
            }
            else if (field.Name is "average_cycle_length" or "average_period_length" or "gestational_weeks" or "baby_development_week")
            {
                var min = field.Name == "average_cycle_length" ? 15 : 1;
                var max = field.Name == "average_cycle_length" ? 90 : field.Name == "average_period_length" ? 15 : 42;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max) return "Quantidade de dias/semanas fora dos limites.";
            }
            else if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 1000)
                return "Texto inválido ou muito longo.";
            else if ((field.Name == "date" || field.Name.EndsWith("_date", StringComparison.Ordinal))
                && !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return "Use uma data explícita no formato YYYY-MM-DD.";
            else if (field.Name.EndsWith("_time", StringComparison.Ordinal)
                && !TimeOnly.TryParseExact(value.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return "Use um horário no formato HH:mm.";
            else if (field.Name == "calendar_month"
                && !DateOnly.TryParseExact(value.GetString() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return "Use um mês no formato YYYY-MM.";
            var schema = Tools.First(tool => tool.Name == name).InputSchema.GetProperty("properties").GetProperty(field.Name);
            if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(choice => choice.ToString() == value.ToString()))
                return "Opção fora dos valores permitidos.";
        }
        return null;
    }
}
