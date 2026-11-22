using System.Globalization;
using System.Text.Json;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed class StructuredRecordSession
{
    public Guid UserId { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed partial class ConversationService
{
    private const string RecordMenu = "Seus registros continuam disponíveis sem usar a cota de IA. Escolha:\n1. Início da menstruação\n2. Fim da menstruação\n3. Fluxo\n4. Sintoma\n5. Relação sexual\n6. Humor\n7. Corrigir início da última menstruação\n8. Notificações (Essencial)\nResponda com o número. Para sair, envie cancelar.";
    private const string NotificationMenu = "Notificações: 1. Consultar  2. Previsão menstrual  3. Pílula  4. Check-in  5. Desativar todas  6. Fuso horário. Para sair, envie cancelar.";

    private async Task<string?> TryStructuredRecordAsync(LumaUser user, string text)
    {
        var normalized = MessageText.Normalize(text).Trim();
        if (normalized is "meu uso" or "meus limites") return await BuildUsageReplyAsync();
        if (normalized is "minha conta" or "suporte" or "privacidade" or "meu calendario" or "notificacoes" or "meus lembretes")
        {
            var baseUrl = (configuration["Luma:WebBaseUrl"] ?? "https://ia-luma.com.br").TrimEnd('/');
            return normalized is "notificacoes" or "meus lembretes" ? await BuildNotificationPreferencesReplyAsync(user)
                : normalized == "meu calendario" ? await BuildCycleCalendarLinkReplyAsync(user, null, Today())
                : normalized == "suporte" ? "Você pode falar com o suporte em " + baseUrl + (usageContext?.AccountId is Guid id ? $"/perfil/{id}/suporte" : "/perfil")
                : normalized == "privacidade" ? "Consulte sua privacidade e as opções da conta em " + baseUrl + "/privacidade"
                : "Acesse sua conta e seu calendário em " + baseUrl + "/perfil";
        }
        if (user.OnboardingStep != OnboardingSteps.Completed || user.ConsentAcceptedAt is null) return null;
        var session = await db.StructuredRecordSessions.SingleOrDefaultAsync(x => x.UserId == user.Id);
        if (session is not null && session.ExpiresAt <= dateProvider.UtcNow)
        {
            if (normalized is "registrar" or "menu" or "registros")
            {
                session.State = "menu";
                session.ExpiresAt = dateProvider.UtcNow.AddHours(24);
                return RecordMenu;
            }
            db.StructuredRecordSessions.Remove(session);
            session = null;
        }
        if (normalized is "registrar" or "menu" or "registros")
        {
            if (session is null) { session = new() { UserId = user.Id }; db.StructuredRecordSessions.Add(session); }
            session.State = "menu";
            session.ExpiresAt = dateProvider.UtcNow.AddHours(24);
            return RecordMenu;
        }
        if (session is null) return null;
        if (normalized == "cancelar") { db.StructuredRecordSessions.Remove(session); return "Fechei o menu sem fazer novos registros."; }
        if (session.State.StartsWith("notify:", StringComparison.Ordinal)) return await StructuredNotificationAsync(user, session, text.Trim());
        if (session.State == "menu")
        {
            if (normalized == "8")
            {
                if (!await HasActiveEssentialSubscriptionAsync(user.PhoneNumber)) return await BuildEssentialFeatureUpgradeReplyAsync(user, "notificações automáticas");
                session.State = "notify:menu";
                return NotificationMenu;
            }
            var tool = normalized switch { "1" => "record_period_start", "2" => "record_period_end", "3" => "record_flow_update", "4" => "record_symptom", "5" => "record_sexual_activity", "6" => "record_mood", "7" => "correct_period_start", _ => null };
            if (tool is null) return RecordMenu;
            session.State = tool;
            return "Em qual data aconteceu? Envie hoje, ontem, anteontem ou DD/MM/AAAA.";
        }
        if (!session.State.Contains('|'))
        {
            var today = Today();
            DateOnly date;
            if (normalized == "hoje") date = today;
            else if (normalized == "ontem") date = today.AddDays(-1);
            else if (normalized is "anteontem" or "antes de ontem") date = today.AddDays(-2);
            else if (!DateOnly.TryParseExact(text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return "Não reconheci a data. Envie hoje, ontem, anteontem ou DD/MM/AAAA.";
            if (date > today || date < today.AddYears(-2)) return "Informe uma data passada de até dois anos atrás, ou hoje.";
            if (session.State == "correct_period_start")
            {
                var cycle = await db.Cycles.Where(x => x.UserId == user.Id).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync();
                if (cycle is null) return "Não há menstruação registrada para corrigir. Envie registrar e escolha início da menstruação.";
                if (cycle.EndDate < date || await db.Cycles.AnyAsync(x => x.UserId == user.Id && x.Id != cycle.Id && x.StartDate >= date))
                    return "Essa data conflita com outro registro do ciclo. Confira o calendário e informe outra data.";
                var starts = await db.CycleEvents.Where(x => x.UserId == user.Id && x.CycleId == cycle.Id && x.Type == CycleEventTypes.PeriodStart).ToListAsync();
                foreach (var start in starts) start.Date = date;
                cycle.StartDate = date;
                cycle.UpdatedAt = dateProvider.UtcNow;
                EnsurePreference(user).LastPeriodStartDate = date;
                db.StructuredRecordSessions.Remove(session);
                return $"Corrigi o início da última menstruação para {date:dd/MM/yyyy}. As previsões do calendário usarão essa referência.";
            }
            if (session.State is "record_flow_update" or "record_symptom" or "record_mood")
            {
                session.State += "|" + date.ToString("yyyy-MM-dd");
                return session.State.StartsWith("record_flow", StringComparison.Ordinal)
                    ? "Como estava o fluxo? 1. Leve  2. Médio  3. Intenso"
                    : "Escreva o sintoma ou humor em poucas palavras (até 80 caracteres).";
            }
            var action = session.State;
            var reply = await ExecuteStructuredToolAsync(user, action, date, null);
            if (action == "record_period_start" && user.PendingAction == PendingActions.AwaitingFlowIntensity)
                session.State = "record_flow_update|" + date.ToString("yyyy-MM-dd");
            else db.StructuredRecordSessions.Remove(session);
            return reply;
        }
        var parts = session.State.Split('|');
        var value = parts[0] == "record_flow_update" ? normalized switch { "1" or "leve" => "light", "2" or "medio" => "medium", "3" or "intenso" => "intense", "4" => "unknown", _ => null } : text.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return "Informe uma opção válida e curta para concluir este registro.";
        var result = await ExecuteStructuredToolAsync(user, parts[0], DateOnly.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture), value);
        db.StructuredRecordSessions.Remove(session);
        return result;
    }

    private async Task<string> StructuredNotificationAsync(LumaUser user, StructuredRecordSession session, string text)
    {
        var original = text;
        text = MessageText.Normalize(text).Trim();
        if (!await HasActiveEssentialSubscriptionAsync(user.PhoneNumber)) return await BuildEssentialFeatureUpgradeReplyAsync(user, "notificações automáticas");
        var parts = session.State.Split(':');
        if (parts[1] == "menu")
        {
            if (text == "1") return await BuildNotificationPreferencesReplyAsync(user) + "\n\n" + NotificationMenu;
            if (text == "5")
            {
                var disabled = await ExecuteBoundToolAsync(user, "Desativar lembretes pelo menu", "disable_notification_preferences", JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
                return disabled.Message + "\n\n" + NotificationMenu;
            }
            if (text == "6") { session.State = "notify:zone"; return "Qual o fuso? Exemplo: America/Sao_Paulo, America/Manaus ou America/Rio_Branco."; }
            var field = text switch { "2" => "period_reminder", "3" => "contraceptive_reminder", "4" => "symptom_checkin", _ => null };
            if (field is null) return NotificationMenu;
            session.State = "notify:" + field + ":action";
            return "O que deseja fazer? 1. Ativar ou alterar horário  2. Desativar";
        }
        var arguments = new Dictionary<string, object?>();
        if (parts[1] == "zone")
        {
            // Preserve the user's original casing: IANA identifiers are case-sensitive on Linux.
            var zone = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(x => string.Equals(x.Id, original, StringComparison.OrdinalIgnoreCase))?.Id ?? original;
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)) return "Não reconheci esse fuso. Informe um identificador como America/Sao_Paulo.";
            arguments["timezone"] = zone;
        }
        else
        {
            var field = parts[1];
            if (parts[2] == "action")
            {
                if (text == "1")
                {
                    if (field == "contraceptive_reminder" && EnsurePreference(user).ContraceptiveType != "pill")
                    {
                        session.State = "notify:" + field + ":pill";
                        return "O lembrete diário é exclusivo para pílula. Você usa pílula anticoncepcional? 1. Sim, uso pílula  2. Não";
                    }
                    session.State = "notify:" + field + ":time";
                    return "Em qual horário? Envie HH:mm, como 07:00. Vou manter o fuso salvo nas suas preferências.";
                }
                if (text != "2") return "Escolha 1 para ativar/alterar ou 2 para desativar.";
                arguments[field + "_enabled"] = false;
            }
            else if (parts[2] == "pill")
            {
                if (text == "2") { session.State = "notify:menu"; return "Não ativei o lembrete da pílula.\n" + NotificationMenu; }
                if (text != "1") return "Confirme: 1. Sim, uso pílula  2. Não";
                session.State = "notify:" + field + ":time:pill";
                return "Qual horário para o lembrete da pílula? Envie HH:mm, como 07:00.";
            }
            else
            {
                if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return "Envie um horário válido no formato HH:mm, como 07:00.";
                arguments[field + "_enabled"] = true;
                arguments[field + "_time"] = text;
                if (parts.Length == 4 && parts[3] == "pill") arguments["contraceptive_type"] = "pill";
            }
        }
        var outcome = await ExecuteBoundToolAsync(user, "Preferências explícitas pelo menu", "update_notification_preferences", JsonSerializer.SerializeToElement(arguments), CancellationToken.None);
        session.State = "notify:menu";
        return outcome.Message + "\n\n" + NotificationMenu;
    }

    private async Task<string> ExecuteStructuredToolAsync(LumaUser user, string name, DateOnly date, string? value)
    {
        var args = new Dictionary<string, object?> { ["date"] = date.ToString("yyyy-MM-dd") };
        if (name == "record_flow_update") args["flow_intensity"] = value;
        if (name == "record_symptom") args["symptom"] = value;
        if (name == "record_mood") args["mood"] = value;
        var outcome = await ExecuteBoundToolAsync(user, "Registro pelo menu", name, JsonSerializer.SerializeToElement(args), CancellationToken.None);
        return PortugueseReplyPolisher.Apply(outcome.Message);
    }

    private async Task BindUsageAsync(string phone, string messageId)
    {
        if (usageContext is null) return;
        if (usageContext.MessageId != messageId) usageContext.Admitted = false;
        var hash = PrivacyRuntime.LookupHash(phone, "account.phone");
        var account = await db.AccountUsers.AsNoTracking().SingleOrDefaultAsync(x => x.PhoneHash == hash && x.DeletionRequestedAt == null);
        usageContext.AccountId = account?.Id;
        usageContext.MessageId = messageId;
        if (account is null) return;
        var now = dateProvider.UtcNow;
        usageContext.Plan = await db.AccountSubscriptions.AsNoTracking()
            .Where(x => x.AccountUserId == account.Id && x.CurrentPeriodEndsAt > now && x.TerminatedAt == null
                && (x.Status == SubscriptionStatuses.Active || x.Status == SubscriptionStatuses.Canceled))
            .OrderByDescending(x => x.PlanCode == "essencial").Select(x => x.PlanCode).FirstOrDefaultAsync() ?? "basico";
    }

    private async Task<string> BuildUsageReplyAsync()
    {
        if (usage is null || usageContext?.AccountId is not Guid account) return "Consulte o uso do seu plano no perfil da Luma.";
        var state = await usage.SnapshotAsync(account, usageContext.Plan, CancellationToken.None);
        var text = $"Hoje você usou {state.DailyUsed} de {state.DailyLimit} mensagens para IA. A cota renova à meia-noite ({state.TimeZone}); não há cota mensal de mensagens.";
        if (state.MonthlyAudioSeconds > 0)
            text += $"\nÁudio neste mês: {state.AudioSecondsUsed} de {state.MonthlyAudioSeconds} segundos. Cada áudio pode ter até {usage.Policy.MaxAudioSeconds} segundos.\nImagens neste mês: {state.ImagesUsed} de {state.MonthlyImages}.";
        return text + "\nO menu registrar e o painel continuam disponíveis após a cota de IA.";
    }
}
