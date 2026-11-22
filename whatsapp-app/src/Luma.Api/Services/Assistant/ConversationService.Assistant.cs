using System.Text.Json;
using Luma.Api.Models;
using Luma.Api.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed partial class ConversationService
{
    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    private async Task<ConversationResult> HandleAssistantIncomingAsync(IncomingMessage incoming)
    {
        var phone = PhoneNumber.Normalize(incoming.From);
        if (_requireActiveSubscription && !await HasActiveSubscriptionAsync(phone))
            return new("Olá! Para conversar com a Luma pelo WhatsApp, é preciso ter um plano ativo vinculado a este número. Acesse sua conta no site, escolha um plano e depois me chame por aqui novamente.");
        if (string.IsNullOrWhiteSpace(incoming.Body) || incoming.Body.Length > 6000)
            return new("Pode me enviar uma mensagem de texto mais curta para continuarmos?");

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        var hash = PrivacyRuntime.LookupHash(phone, "user.phone");
        if (transaction is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({hash}))");
        var messageId = incoming.ProviderMessageId ?? Guid.NewGuid().ToString();
        var previous = await db.AssistantTurns.AsNoTracking()
            .FirstOrDefaultAsync(turn => turn.Provider == incoming.Provider && turn.MessageId == messageId);
        if (previous is not null)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == previous.UserId && user.PhoneHash == hash);
            return owner is null ? new("Não consegui validar essa mensagem.") : new(previous.Reply, previous.MediaUrl);
        }
        var user = await db.Users.Include(item => item.Preference).SingleOrDefaultAsync(item => item.PhoneHash == hash);
        var newUser = user is null;
        if (user is null)
        {
            user = new LumaUser { PhoneNumber = phone, Preference = new UserPreference() };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        _mediaUrlForCurrentReply = null;
        await BindUsageAsync(phone, messageId);
        if (usage is not null && _requireActiveSubscription && usageContext?.AccountId is null)
            return new("Não consegui validar a conta vinculada a este número. Acesse seu perfil ou fale com o suporte.");
        AssistantReply reply;
        if (newUser)
            reply = new(InitialConsentMessage(), null);
        else if (user.OnboardingStep == OnboardingSteps.UnderageBlocked)
            reply = new("Por segurança, a Luma não pode continuar esse cadastro para menores de 18 anos.", null);
        else if (await TryStructuredRecordAsync(user, incoming.Body) is { } structured)
            reply = new(structured, null);
        else if (ConversationTopicPolicy.IsClearlyExternal(incoming.Body))
            reply = new(ConversationTopicPolicy.Reply, null);
        else
        {
            if (usage is not null && usageContext?.AccountId is Guid accountId && !usageContext.Admitted)
            {
                var admission = await usage.ReserveAsync(accountId, usageContext.Plan, messageId,
                    user.OnboardingStep == OnboardingSteps.Completed ? "ai" : "onboarding", 1);
                if (!admission.Allowed) return new(admission.Message!);
                if (admission.Duplicate) return new("Essa mensagem já foi recebida. Confira seu histórico ou envie registrar para continuar sem repetir uma chamada à IA.");
                usageContext.Admitted = true;
            }
            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            var history = user.ConsentAcceptedAt is null ? [] : await db.AssistantTurns
                .Where(turn => turn.UserId == user.Id && turn.CreatedAt >= cutoff)
                .OrderByDescending(turn => turn.CreatedAt).Take(4).ToListAsync();
            var memory = string.Join("\n", history.AsEnumerable().Reverse().Select(turn => "Usuária: " + turn.Request[..Math.Min(turn.Request.Length, 600)] + "\nLuma: " + turn.Reply[..Math.Min(turn.Reply.Length, 600)]));
            reply = await assistant!.RespondAsync(new(incoming.Body, await AssistantContextAsync(user), Today(), memory),
                async (name, arguments, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var result = await ExecuteBoundToolAsync(user, incoming.Body, name, arguments, ct);
                    if (result.Success && name == "record_period_start" && user.PendingAction == PendingActions.AwaitingFlowIntensity)
                    {
                        var flowSession = await db.StructuredRecordSessions.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
                        if (flowSession is null) { flowSession = new() { UserId = user.Id }; db.StructuredRecordSessions.Add(flowSession); }
                        flowSession.State = "record_flow_update|" + arguments.GetProperty("date").GetString();
                        flowSession.ExpiresAt = dateProvider.UtcNow.AddHours(24);
                    }
                    await db.SaveChangesAsync();
                    var authoritativeWrite = name.StartsWith("record_", StringComparison.Ordinal)
                        || name.StartsWith("update_", StringComparison.Ordinal) || name.StartsWith("complete_", StringComparison.Ordinal)
                        || name.StartsWith("disable_", StringComparison.Ordinal) || name is "start_pregnancy_mode" or "confirm_pending_intent" or "generate_baby_size_image";
                    return result with { State = await AssistantContextAsync(user), Fixed = result.Fixed || authoritativeWrite };
                });
            if (reply.RollbackWrites)
            {
                if (transaction is not null) await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                return new("Não concluí os registros desta mensagem. " + reply.Body);
            }
        }
        db.AssistantTurns.Add(new AssistantTurnRecord
        {
            UserId = user.Id, Provider = incoming.Provider, MessageId = messageId,
            Request = user.ConsentAcceptedAt is null ? "" : incoming.Body,
            Reply = reply.Body, MediaUrl = reply.MediaUrl
        });
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var expired = await db.AssistantTurns.Where(turn => turn.UserId == user.Id && turn.CreatedAt < DateTimeOffset.UtcNow.AddDays(-30)).ToListAsync();
        db.AssistantTurns.RemoveRange(expired);
        await db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        return new(reply.Body, reply.MediaUrl);
    }

    private async Task<string> AssistantContextAsync(LumaUser user)
    {
        var pending = user.ConsentAcceptedAt is null ? null : await GetLatestPendingIntentAsync(user.Id);
        var pregnancy = user.ConsentAcceptedAt is null ? null : await GetActivePregnancyAsync(user.Id);
        var accountPhoneHash = PrivacyRuntime.LookupHash(user.PhoneNumber, "account.phone");
        var now = DateTimeOffset.UtcNow;
        var subscription = user.ConsentAcceptedAt is null ? null : await db.AccountSubscriptions.AsNoTracking()
            .Where(item => item.PhoneHash == accountPhoneHash && item.CurrentPeriodEndsAt >= now
                && (item.Status == SubscriptionStatuses.Active || item.Status == SubscriptionStatuses.Canceled))
            .OrderByDescending(item => item.CurrentPeriodEndsAt)
            .Select(item => new { item.PlanCode, item.BillingInterval, item.Status, item.CurrentPeriodEndsAt }).FirstOrDefaultAsync();
        return JsonSerializer.Serialize(new
        {
            today = Today(), name = user.DisplayName, stage = user.OnboardingStep, pending_action = user.PendingAction,
            consent = user.ConsentAcceptedAt is not null, adult = user.IsAdultConfirmed,
            subscription,
            usage_policy = usage is null ? null : new
            {
                basic_daily_messages = usage.Policy.BasicDailyMessages,
                essential_daily_messages = usage.Policy.EssentialDailyMessages,
                monthly_message_cap = false,
                monthly_audio_seconds = usage.Policy.MonthlyAudioSeconds,
                max_audio_seconds = usage.Policy.MaxAudioSeconds,
                monthly_images = usage.Policy.MonthlyImages,
                resets_time_zone = usage.Policy.TimeZone,
                records_without_ai = "registrar"
            },
            preferences = user.ConsentAcceptedAt is null || user.Preference is null ? null : new
            {
                user.Preference.LastPeriodStartDate, user.Preference.AverageCycleLength,
                user.Preference.AveragePeriodLength, user.Preference.ContraceptiveType
            },
            pregnancy = pregnancy is null ? null : new { pregnancy.LastPeriodDate, pregnancy.EstimatedDueDate, pregnancy.GestationalWeeksAtRegistration },
            pending_intent = pending is null ? null : new { pending.Intent, pending.Date },
            next_question = user.OnboardingStep == OnboardingSteps.Completed ? null : NextOnboardingPrompt(user, null)
        }, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
    }

    private async Task<ToolOutcome> ExecuteBoundToolAsync(LumaUser user, string message, string name, JsonElement arguments, CancellationToken ct)
    {
        var validation = LumaToolCatalog.Validate(name, arguments);
        if (validation is not null) return new(false, validation);
        if (name == "medical_guardrail") return new(true, SafetyGuardrail.SafeReply, true);
        if (name == "out_of_scope") return new(true, OutOfScopeMessage(), true);
        if (name == "ask_clarification") return new(true, arguments.GetProperty("question").GetString()!);
        if (name == "get_onboarding_state") return new(true, await AssistantContextAsync(user), ContextOnly: true);
        if (name == "search_luma_knowledge_base")
            return knowledge is null ? new(false, KnowledgeStore.NoEvidence, true) : await knowledge.SearchAsync(arguments.GetProperty("query").GetString()!, ct,
                arguments.TryGetProperty("scope", out var scope) ? scope.GetString() ?? "all" : "all");
        if (name != "complete_onboarding_step" && user.ConsentAcceptedAt is null)
            return new(false, InitialConsentMessage(), true);
        if (user.OnboardingStep == OnboardingSteps.UnderageBlocked)
            return new(false, "Por segurança, não posso continuar esse cadastro para menores de 18 anos.", true);
        if (name == "get_user_profile")
            return new(true, await AssistantContextAsync(user), ContextOnly: true);
        if (name == "get_usage_summary") return new(true, await BuildUsageReplyAsync(), true);
        if (name == "confirm_pending_intent")
        {
            if (user.OnboardingStep != OnboardingSteps.Completed) return new(false, NextOnboardingPrompt(user, null));
            var pending = await GetLatestPendingIntentAsync(user.Id);
            if (pending is null) return new(false, "Não há registro pendente para confirmar.");
            if (!arguments.GetProperty("confirmed").GetBoolean())
            {
                pending.Status = PendingIntentStatus.Dismissed;
                pending.CompletedAt = pending.UpdatedAt = DateTimeOffset.UtcNow;
                return new(true, "Tudo bem, descartei aquela intenção sem registrar o evento.");
            }
            using var payload = JsonDocument.Parse(pending.PayloadJson);
            var result = payload.RootElement.TryGetProperty("mcp_tool", out var pendingTool)
                ? await ExecuteBoundToolAsync(user, message, pendingTool.GetString()!, payload.RootElement.GetProperty("arguments"), ct)
                : new ToolOutcome(true, await ExecutePendingIntentAsync(user, pending, Today()));
            if (result.Success)
            {
                pending.Status = PendingIntentStatus.Completed;
                pending.CompletedAt = pending.UpdatedAt = DateTimeOffset.UtcNow;
            }
            return result;
        }
        var data = JsonSerializer.Deserialize<LumaToolCall>(arguments, ToolJson) ?? new();
        data.ToolName = name;
        var today = Today();
        if (data.Date > today && name is not ("record_prenatal_appointment" or "record_ultrasound"))
            return new(false, "Essa data ficou no futuro. Pode confirmar a data do ocorrido?");
        if (name == "complete_onboarding_step")
        {
            if (data.AverageCycleLength is not null && data.AverageCycleLength is < 15 or > 90
                || data.AveragePeriodLength is not null && data.AveragePeriodLength is < 1 or > 15)
                return new(false, "Pode confirmar a duração do ciclo e da menstruação?");
            if (data.ConsentAccepted == false && user.OnboardingStep is OnboardingSteps.AwaitingConsent or OnboardingSteps.ConsentDeclined)
            {
                user.OnboardingStep = OnboardingSteps.ConsentDeclined;
                return new(true, "Tudo bem. Sem sua autorização não vou registrar dados de saúde. Se decidir continuar, pode me avisar.", true);
            }
            if (user.ConsentAcceptedAt is null && data.ConsentAccepted != true) return new(false, InitialConsentMessage(), true);
            var result = await ExecuteOnboardingToolAsync(user, data);
            return new(result is not null, result ?? NextOnboardingPrompt(user, null),
                user.OnboardingStep is OnboardingSteps.UnderageBlocked or OnboardingSteps.ConsentDeclined);
        }
        if (user.OnboardingStep != OnboardingSteps.Completed)
        {
            if (name.StartsWith("record_", StringComparison.Ordinal) || name == "start_pregnancy_mode")
            {
                var pending = await SavePendingIntentAsync(user.Id, new ConversationIntent { Intent = name, Date = data.Date ?? today }, "", today);
                pending.PayloadJson = JsonSerializer.Serialize(new { mcp_tool = name, arguments });
                return new(true, "Entendi o registro que você quer fazer. Guardei essa intenção para confirmar depois de terminar o cadastro. " + NextOnboardingPrompt(user, null));
            }
            return new(false, "Vamos terminar seu cadastro antes dessa ação. " + NextOnboardingPrompt(user, null));
        }
        if (name == "update_user_preferences")
        {
            var preference = EnsurePreference(user);
            if (data.AverageCycleLength is not null) preference.AverageCycleLength = data.AverageCycleLength.Value;
            if (data.AveragePeriodLength is not null) preference.AveragePeriodLength = data.AveragePeriodLength.Value;
            if (data.ContraceptiveType is not null)
            {
                preference.ContraceptiveType = data.ContraceptiveType;
                preference.UsesHormonalContraceptive = data.ContraceptiveType is "pill" or "injection" or "implant" or "hormonal_iud";
            }
            return new(true, $"Preferências salvas. Ciclo habitual: {preference.AverageCycleLength} dias. Menstruação habitual: {preference.AveragePeriodLength} dias. Isso não encerrou a menstruação atual.");
        }
        if (name == "get_pregnancy_due_date") return new(true, await BuildPregnancyDueDateReplyAsync(user.Id));
        if (name == "get_pregnancy_weeks") return new(true, await BuildPregnancyWeeksReplyAsync(user.Id, today));
        if (name == "get_fertile_window") return new(true, BuildFertileWindowReply(user, today));
        if (name == "record_period_end" && data.Date is { } endDate)
        {
            var cycle = await db.Cycles.Where(x => x.UserId == user.Id && x.Status == CycleStatus.Ongoing)
                .OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(ct);
            if (cycle is not null && endDate < cycle.StartDate)
                return new(false, "A data de término ficou antes do início da menstruação. Pode confirmar as datas?");
        }
        if (name == "start_pregnancy_mode" && data.GestationalWeeks is null && data.LastPeriodDate is null && data.EstimatedDueDate is null)
            return new(false, "Obrigada por me contar. Você sabe de quantas semanas está, a DPP informada no pré-natal ou a data da última menstruação?");
        if (name == "update_notification_preferences")
        {
            if (!await HasActiveEssentialSubscriptionAsync(user.PhoneNumber))
                return new(false, await BuildEssentialFeatureUpgradeReplyAsync(user, "notificações automáticas"), true);
            if (data.TimeZone is not null && !TimeZoneInfo.TryFindSystemTimeZoneById(data.TimeZone, out _))
                return new(false, "Não reconheci esse fuso horário. Pode informar sua cidade ou região?");
            if (data.ContraceptiveReminderEnabled == true && (data.ContraceptiveType ?? EnsurePreference(user).ContraceptiveType) != "pill")
                return new(false, "O lembrete diário é para pílula. Você usa pílula ou outro método?");
            if (data.ContraceptiveType is not null)
            {
                EnsurePreference(user).ContraceptiveType = data.ContraceptiveType;
                EnsurePreference(user).UsesHormonalContraceptive = data.ContraceptiveType is "pill" or "injection" or "implant" or "hormonal_iud";
            }
            var result = await ExecuteUpdateNotificationPreferencesToolAsync(user, data);
            var preference = await db.NotificationPreferences.FirstOrDefaultAsync(item => item.UserId == user.Id, ct);
            if (preference is not null)
            {
                if (data.PeriodReminderTime is not null) preference.PeriodReminderTime = TimeOnly.ParseExact(data.PeriodReminderTime, "HH:mm");
                if (data.ContraceptiveReminderTime is not null) preference.ContraceptiveReminderTime = TimeOnly.ParseExact(data.ContraceptiveReminderTime, "HH:mm");
                if (data.SymptomCheckinTime is not null) preference.SymptomCheckinTime = TimeOnly.ParseExact(data.SymptomCheckinTime, "HH:mm");
                return new(true, await BuildNotificationPreferencesReplyAsync(user));
            }
            return new(false, result);
        }
        var reply = await ExecuteAgentToolAsync(user, message, MessageText.Normalize(message), data, today);
        if (reply is null) return new(false, "Não consegui executar esse registro com esses dados. Pode esclarecer?");
        return new(true, reply, name == "record_pregnancy_bleeding" || IsSubscriptionRestrictionReply(reply) || IsOperationalAsyncReply(reply),
            _mediaUrlForCurrentReply);
    }
}
