using System.Text.Json;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed class ConversationService(
    LumaDbContext db,
    IConfiguration configuration,
    IOnboardingDataExtractor onboardingAi,
    IConversationIntentExtractor conversationAi,
    ILumaToolAgent toolAgent,
    ILumaResponseGenerator responseGenerator,
    IDateProvider dateProvider,
    ILogger<ConversationService> logger)
{
    private readonly bool _storeMessageBodies = configuration.GetValue("Luma:StoreMessageBodies", false);

    public async Task<string> HandleIncomingMessageAsync(IncomingMessage incoming)
    {
        var phone = PhoneNumber.Normalize(incoming.From);
        var user = await db.Users
            .Include(existing => existing.Preference)
            .FirstOrDefaultAsync(existing => existing.PhoneNumber == phone);

        if (user is null)
        {
            user = new LumaUser
            {
                PhoneNumber = phone,
                OnboardingStep = OnboardingSteps.AwaitingConsent,
                Preference = new UserPreference()
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        db.Messages.Add(new ConversationMessage
        {
            UserId = user.Id,
            Direction = "inbound",
            Provider = incoming.Provider,
            ProviderMessageId = incoming.ProviderMessageId,
            Body = _storeMessageBodies ? incoming.Body : null
        });

        var backendReply = await BuildReplyAsync(user, incoming.Body);
        var reply = await BuildFinalReplyAsync(user, incoming.Body, backendReply);

        db.Messages.Add(new ConversationMessage
        {
            UserId = user.Id,
            Direction = "outbound",
            Provider = incoming.Provider,
            Body = _storeMessageBodies ? reply : null
        });

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Processed message for user {UserId} at step {Step}", user.Id, user.OnboardingStep);

        return reply;
    }

    private async Task<string> BuildFinalReplyAsync(LumaUser user, string rawBody, string backendReply)
    {
        var body = MessageText.Normalize(rawBody);
        var isGuardrail = IsFixedGuardrailReply(backendReply);
        if (isGuardrail || IsRequiredBackendPrompt(user, backendReply))
        {
            return backendReply;
        }

        var knowledge = LumaKnowledgeBase.Search(body);

        return await responseGenerator.GenerateAsync(new LumaResponseRequest(
            UserMessage: rawBody,
            BackendResult: backendReply,
            OnboardingStep: user.OnboardingStep,
            DisplayName: user.DisplayName,
            PendingAction: user.PendingAction,
            IsGuardrail: isGuardrail,
            AvailableTools: LumaTools.Available,
            Knowledge: knowledge));
    }

    private async Task<string> BuildReplyAsync(LumaUser user, string rawBody)
    {
        var body = MessageText.Normalize(rawBody);

        var agentReply = await TryHandleAgentToolAsync(user, rawBody, body, Today());
        if (agentReply is not null)
        {
            return agentReply;
        }

        if (user.OnboardingStep is OnboardingSteps.ConsentDeclined)
        {
            return IsAffirmative(body) ? await AcceptConsentAsync(user) : InitialConsentMessage();
        }

        if (user.OnboardingStep is OnboardingSteps.UnderageBlocked)
        {
            return "Por seguranca, a Luma ainda nao pode continuar esse cadastro pelo WhatsApp para menores de 18 anos.";
        }

        if (user.OnboardingStep != OnboardingSteps.Completed)
        {
            return await ContinueOnboardingAsync(user, body, rawBody);
        }

        var intent = await ExtractConversationIntentAsync(body, rawBody, Today(), user);

        if (SafetyGuardrail.ShouldBlock(body)
            && (intent.Intent != ConversationIntents.PregnancyPositive || !IsPregnancyPositiveStatement(body)))
        {
            return SafetyGuardrail.SafeReply;
        }

        if (user.PendingAction == PendingActions.AwaitingFlowIntensity)
        {
            var pendingFlow = ParseFlowIntensity(body);
            if (pendingFlow is not null && IsFlowOnlyResponse(body))
            {
                user.PendingAction = null;
                await AddOrReplaceFlowEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), Today(), pendingFlow);

                return pendingFlow == "unknown"
                    ? "Tudo bem, deixei o fluxo de hoje sem informar."
                    : $"Atualizei aqui. Hoje ficou registrado como fluxo {FlowLabel(pendingFlow)}.";
            }

            if (!LooksLikeKnownCompletedIntent(body, intent))
            {
                return "Como esta o fluxo?\n1. Leve\n2. Medio\n3. Intenso\n4. Prefiro nao informar";
            }

            user.PendingAction = null;
        }

        return await HandleCompletedUserMessageAsync(user, body, rawBody, intent);
    }

    private async Task<string?> TryHandleAgentToolAsync(LumaUser user, string rawBody, string body, DateOnly today)
    {
        if (SafetyGuardrail.ShouldBlock(body) && !IsPregnancyPositiveStatement(body))
        {
            return SafetyGuardrail.SafeReply;
        }

        if (user.OnboardingStep == OnboardingSteps.AwaitingConsent && IsGreeting(body))
        {
            return null;
        }

        var decision = await toolAgent.DecideAsync(new LumaToolAgentRequest(
            UserMessage: rawBody,
            Today: today,
            Context: BuildConversationContext(user),
            Knowledge: LumaKnowledgeBase.Search(body),
            AvailableTools: LumaTools.Available));

        if (decision?.ToolName is null)
        {
            return null;
        }

        return await ExecuteAgentToolAsync(user, rawBody, body, decision, today);
    }

    private async Task<string?> ExecuteAgentToolAsync(LumaUser user, string rawBody, string body, LumaToolCall tool, DateOnly today)
    {
        switch (tool.ToolName)
        {
            case "medical_guardrail":
                return SafetyGuardrail.SafeReply;

            case "out_of_scope":
                return OutOfScopeMessage();

            case "search_luma_knowledge_base":
                return LumaKnowledgeBase.Search(body) ?? LumaIdentityMessage();

            case "complete_onboarding_step":
                if (user.OnboardingStep == OnboardingSteps.AwaitingConsent && IsGreeting(body))
                {
                    return null;
                }

                return await ExecuteOnboardingToolAsync(user, tool);

            case "record_period_start":
                return await ExecuteRecordPeriodStartToolAsync(user, tool, today);

            case "record_period_end":
                return await ExecuteRecordPeriodEndToolAsync(user, tool, today);

            case "record_flow_update":
                if (tool.FlowIntensity is null)
                {
                    return null;
                }

                await AddOrReplaceFlowEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), tool.Date ?? today, tool.FlowIntensity);
                return tool.FlowIntensity == "unknown"
                    ? "Tudo bem, deixei o fluxo sem informar."
                    : $"Registrei o fluxo {FlowLabel(tool.FlowIntensity)} para {FormatDate(tool.Date ?? today)}.";

            case "record_symptom":
                if (tool.Symptom is null)
                {
                    return null;
                }

                await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.Symptom, tool.Date ?? today, new
                {
                    symptom = tool.Symptom,
                    intensity = tool.Intensity ?? "moderate"
                });
                return $"Registrei {SymptomLabel(tool.Symptom)} para {FormatDate(tool.Date ?? today)}. Se vier com dor forte, febre, tontura ou mal-estar importante, procure orientacao medica.";

            case "record_mood":
                if (tool.Mood is null)
                {
                    return null;
                }

                await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.Mood, tool.Date ?? today, new { mood = tool.Mood });
                return $"Registrei esse humor para {FormatDate(tool.Date ?? today)} como historico do seu ciclo.";

            case "record_sexual_activity":
                return await ExecuteRecordSexualActivityToolAsync(user, tool, today);

            case "start_pregnancy_mode":
                return await HandlePregnancyPositiveAsync(user, new ConversationIntent
                {
                    Intent = ConversationIntents.PregnancyPositive,
                    Date = tool.Date ?? today,
                    GestationalWeeks = tool.GestationalWeeks,
                    LastPeriodDate = tool.LastPeriodDate,
                    EstimatedDueDate = tool.EstimatedDueDate
                }, today);

            case "record_pregnancy_bleeding":
                return await HandlePregnancyBleedingAsync(user.Id, tool.Date ?? today);

            case "record_pregnancy_symptom":
                var activePregnancy = await GetActivePregnancyAsync(user.Id);
                if (activePregnancy is null || tool.Symptom is null)
                {
                    return null;
                }

                await AddCycleEventAsync(user.Id, null, CycleEventTypes.PregnancySymptom, tool.Date ?? today, new
                {
                    pregnancy_id = activePregnancy.Id,
                    symptom = tool.Symptom,
                    intensity = tool.Intensity ?? "moderate"
                });
                return "Registrei esse sintoma na sua gravidez. Se vier com dor forte, febre, tontura, sangramento, perda de liquido ou mal-estar importante, fale com seu medico ou obstetra.";

            case "record_prenatal_appointment":
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.PrenatalAppointment, tool.Date ?? today, new { });
                return $"Registrei sua consulta de pre-natal em {FormatDate(tool.Date ?? today)}.";

            case "record_ultrasound":
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.Ultrasound, tool.Date ?? today, new { });
                return $"Registrei o ultrassom em {FormatDate(tool.Date ?? today)}.";

            case "calculate_next_period":
                return BuildNextPeriodReply(user);

            case "calculate_delay":
                return BuildDelayReply(user, today);

            case "get_last_period":
                return BuildLastPeriodReply(user);

            case "get_last_symptom":
                return await BuildLastSymptomReplyAsync(user.Id);

            case "get_last_sexual_activity":
                return await BuildLastSexualActivityReplyAsync(user.Id);

            default:
                return null;
        }
    }

    private async Task<string?> ExecuteOnboardingToolAsync(LumaUser user, LumaToolCall tool)
    {
        var extraction = new OnboardingExtraction
        {
            DisplayName = tool.DisplayName,
            IsAdultConfirmed = tool.IsAdultConfirmed,
            LastPeriodStartDate = tool.LastPeriodDate ?? tool.Date,
            AverageCycleLength = tool.AverageCycleLength,
            AveragePeriodLength = tool.AveragePeriodLength,
            ContraceptiveType = tool.ContraceptiveType
        };

        if (user.OnboardingStep is OnboardingSteps.AwaitingConsent or OnboardingSteps.ConsentDeclined
            && tool.ConsentAccepted == true)
        {
            return await AcceptConsentAsync(user);
        }

        if (user.OnboardingStep == OnboardingSteps.AwaitingDisplayName && extraction.DisplayName is not null)
        {
            await ApplyExtractedOnboardingDataAsync(user, extraction);
            return NextOnboardingPrompt(user, extraction);
        }

        if (user.OnboardingStep is not OnboardingSteps.Completed && extraction.HasAnyValue())
        {
            await ApplyExtractedOnboardingDataAsync(user, extraction);
            return user.OnboardingStep == OnboardingSteps.Completed
                ? await CompleteOnboardingWithPendingPromptAsync(user, extraction)
                : NextOnboardingPrompt(user, extraction);
        }

        return null;
    }

    private async Task<string> ExecuteRecordPeriodStartToolAsync(LumaUser user, LumaToolCall tool, DateOnly today)
    {
        var date = tool.Date ?? today;
        if (user.OnboardingStep != OnboardingSteps.Completed && user.OnboardingStep != OnboardingSteps.AwaitingLastPeriodStart)
        {
            var pending = await SavePendingIntentAsync(user.Id, new ConversationIntent
            {
                Intent = ConversationIntents.PeriodStart,
                Date = date
            }, string.Empty, today);

            return PendingIntentCapturedMessage(user, pending, today);
        }

        var cycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, date);
        EnsurePreference(user).LastPeriodStartDate = date;
        await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodStart, date, new { agent_tool = true });

        if (user.OnboardingStep == OnboardingSteps.AwaitingLastPeriodStart)
        {
            user.OnboardingStep = OnboardingSteps.AwaitingAverageCycleLength;
            return NextOnboardingPrompt(user, new OnboardingExtraction { LastPeriodStartDate = date });
        }

        if (tool.FlowIntensity is not null)
        {
            await AddOrReplaceFlowEventAsync(user.Id, cycle.Id, date, tool.FlowIntensity);
            return $"Registrei o inicio da sua menstruacao em {FormatDate(date)} com fluxo {FlowLabel(tool.FlowIntensity)}.";
        }

        user.PendingAction = PendingActions.AwaitingFlowIntensity;
        return $"Registrei o inicio da sua menstruacao em {FormatDate(date)}.\n\nComo esta o fluxo?\n1. Leve\n2. Medio\n3. Intenso\n4. Prefiro nao informar";
    }

    private async Task<string?> ExecuteRecordPeriodEndToolAsync(LumaUser user, LumaToolCall tool, DateOnly today)
    {
        if (user.OnboardingStep != OnboardingSteps.Completed)
        {
            return null;
        }

        var date = tool.Date ?? today;
        var cycle = await db.Cycles
            .Where(existing => existing.UserId == user.Id && existing.Status == CycleStatus.Ongoing)
            .OrderByDescending(existing => existing.StartDate)
            .FirstOrDefaultAsync();

        if (cycle is null)
        {
            await AddCycleEventAsync(user.Id, null, CycleEventTypes.PeriodEnd, date, new { needs_period_start = true, agent_tool = true });
            return $"Registrei que sua menstruacao terminou em {FormatDate(date)}. Ainda nao encontrei um ciclo aberto para calcular a duracao; se puder, me diga quando ela comecou.";
        }

        if (date < cycle.StartDate)
        {
            return $"A data de termino ficou antes do inicio do ciclo ({FormatDate(cycle.StartDate)}). Pode me mandar a data de termino de novo?";
        }

        cycle.EndDate = date;
        cycle.Status = CycleStatus.Finished;
        cycle.UpdatedAt = DateTimeOffset.UtcNow;
        await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodEnd, date, new { agent_tool = true });
        var days = Math.Max(1, date.DayNumber - cycle.StartDate.DayNumber + 1);
        var nextPeriod = cycle.StartDate.AddDays(EnsurePreference(user).AverageCycleLength);
        return $"Registrei que sua menstruacao terminou em {FormatDate(date)}. Ela durou cerca de {days} dias neste ciclo. Pela sua media atual, a proxima menstruacao esta prevista para perto de {FormatDate(nextPeriod)}.";
    }

    private async Task<string> ExecuteRecordSexualActivityToolAsync(LumaUser user, LumaToolCall tool, DateOnly today)
    {
        var date = tool.Date ?? today;
        if (user.OnboardingStep != OnboardingSteps.Completed)
        {
            var pending = await SavePendingIntentAsync(user.Id, new ConversationIntent
            {
                Intent = ConversationIntents.SexualActivity,
                Date = date,
                Protected = tool.Protected
            }, string.Empty, today);

            return PendingIntentCapturedMessage(user, pending, today);
        }

        await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.SexualActivity, date, new
        {
            protected_value = tool.Protected ?? "unknown",
            @protected = tool.Protected ?? "unknown",
            contraceptive_method = "unknown",
            agent_tool = true
        });

        return $"Registrei a relacao em {FormatDate(date)}. Esse dado fica salvo apenas para seu historico; eu nao uso isso para afirmar gravidez ou diagnostico.";
    }

    private async Task<string> ContinueOnboardingAsync(LumaUser user, string body, string rawBody)
    {
        var today = Today();

        switch (user.OnboardingStep)
        {
            case OnboardingSteps.AwaitingConsent:
                if (IsAffirmative(body))
                {
                    return await AcceptConsentAsync(user);
                }

                if (IsNegative(body))
                {
                    user.OnboardingStep = OnboardingSteps.ConsentDeclined;
                    return "Tudo bem. Sem o seu consentimento eu nao posso armazenar dados do ciclo ou continuar o cadastro. Se mudar de ideia, responda \"aceito\".";
                }

                return InitialConsentMessage();

            case OnboardingSteps.AwaitingDisplayName:
                var extractedFromNameStep = await ExtractOnboardingDataAsync(body, rawBody, today);
                if (extractedFromNameStep.HasAnyValue())
                {
                    await ApplyExtractedOnboardingDataAsync(user, extractedFromNameStep);
                    if (user.OnboardingStep != OnboardingSteps.AwaitingDisplayName)
                    {
                        return NextOnboardingPrompt(user, extractedFromNameStep);
                    }
                }

                var name = rawBody.Trim();
                if (!IsLikelyPlainDisplayName(name))
                {
                    var outOfOrderReply = await TryCaptureOutOfOrderIntentAsync(user, body, rawBody, today);
                    if (outOfOrderReply is not null)
                    {
                        return outOfOrderReply;
                    }

                    return MisunderstoodMessage("Pode responder so com seu primeiro nome ou apelido. Por exemplo: \"Nay\" ou \"Pode me chamar de Nay\".");
                }

                user.DisplayName = name;
                user.OnboardingStep = OnboardingSteps.AwaitingAgeConfirmation;
                return NextOnboardingPrompt(user, new OnboardingExtraction { DisplayName = name });

            case OnboardingSteps.AwaitingAgeConfirmation:
                var extractedFromAgeStep = await ExtractOnboardingDataAsync(body, rawBody, today);
                if (extractedFromAgeStep.IsAdultConfirmed is not null)
                {
                    await ApplyExtractedOnboardingDataAsync(user, extractedFromAgeStep);
                    return NextOnboardingPrompt(user, extractedFromAgeStep);
                }

                if (IsAffirmative(body))
                {
                    user.IsAdultConfirmed = true;
                    user.OnboardingStep = OnboardingSteps.AwaitingLastPeriodStart;
                    return NextOnboardingPrompt(user, new OnboardingExtraction { IsAdultConfirmed = true });
                }

                if (IsNegative(body))
                {
                    user.IsAdultConfirmed = false;
                    user.OnboardingStep = OnboardingSteps.UnderageBlocked;
                    return "Obrigada por responder. Por seguranca, a Luma ainda nao pode continuar esse cadastro pelo WhatsApp para menores de 18 anos.";
                }

                var ageStepOutOfOrderReply = await TryCaptureOutOfOrderIntentAsync(user, body, rawBody, today);
                if (ageStepOutOfOrderReply is not null)
                {
                    return ageStepOutOfOrderReply;
                }

                return MisunderstoodMessage("Voce pode responder \"sim\", \"nao\" ou algo como \"tenho 23 anos\".");

            case OnboardingSteps.AwaitingLastPeriodStart:
                var extractedFromPeriodStep = await ExtractOnboardingDataAsync(body, rawBody, today);
                if (extractedFromPeriodStep.LastPeriodStartDate is not null || extractedFromPeriodStep.LastPeriodUnknown)
                {
                    await ApplyExtractedOnboardingDataAsync(user, extractedFromPeriodStep);
                    return NextOnboardingPrompt(user, extractedFromPeriodStep);
                }

                if (DateParser.IsUnknown(body))
                {
                    EnsurePreference(user).LastPeriodStartDate = null;
                    user.OnboardingStep = OnboardingSteps.AwaitingAverageCycleLength;
                    return NextOnboardingPrompt(user, new OnboardingExtraction { LastPeriodUnknown = true });
                }

                var date = DateParser.ParseFlexibleDate(rawBody, today);
                if (date is null)
                {
                    var periodStepOutOfOrderReply = await TryCaptureOutOfOrderIntentAsync(user, body, rawBody, today);
                    if (periodStepOutOfOrderReply is not null)
                    {
                        return periodStepOutOfOrderReply;
                    }

                    return MisunderstoodMessage("Pode responder como \"10/04\", \"dia 10\", \"comecou ha 3 dias\", \"ontem\" ou \"nao lembro\".");
                }

                EnsurePreference(user).LastPeriodStartDate = date.Value;
                var onboardingCycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, date.Value, CycleStatus.Unknown);
                await AddCycleEventAsync(user.Id, onboardingCycle.Id, CycleEventTypes.PeriodStart, date.Value, new { onboarding = true });
                user.OnboardingStep = OnboardingSteps.AwaitingAverageCycleLength;
                return NextOnboardingPrompt(user, new OnboardingExtraction { LastPeriodStartDate = date.Value });

            case OnboardingSteps.AwaitingAverageCycleLength:
                var extractedFromCycleStep = await ExtractOnboardingDataAsync(body, rawBody, today);
                if (extractedFromCycleStep.AverageCycleLength is not null)
                {
                    await ApplyExtractedOnboardingDataAsync(user, extractedFromCycleStep);
                    return NextOnboardingPrompt(user, extractedFromCycleStep);
                }

                var cycleLength = MessageText.ExtractFirstInteger(body);
                if (cycleLength is null && DateParser.IsUnknown(body))
                {
                    cycleLength = 28;
                }

                if (cycleLength is < 21 or > 45 or null)
                {
                    var cycleStepOutOfOrderReply = await TryCaptureOutOfOrderIntentAsync(user, body, rawBody, today);
                    if (cycleStepOutOfOrderReply is not null)
                    {
                        return cycleStepOutOfOrderReply;
                    }

                    return MisunderstoodMessage("Me diga um numero entre 21 e 45 dias. Se nao souber, responda \"nao sei\" e uso 28 dias por enquanto.");
                }

                EnsurePreference(user).AverageCycleLength = cycleLength.Value;
                user.OnboardingStep = OnboardingSteps.AwaitingAveragePeriodLength;
                return NextOnboardingPrompt(user, new OnboardingExtraction { AverageCycleLength = cycleLength.Value });

            case OnboardingSteps.AwaitingAveragePeriodLength:
                var extractedFromPeriodLengthStep = await ExtractOnboardingDataAsync(body, rawBody, today);
                if (extractedFromPeriodLengthStep.AveragePeriodLength is not null)
                {
                    await ApplyExtractedOnboardingDataAsync(user, extractedFromPeriodLengthStep);
                    return NextOnboardingPrompt(user, extractedFromPeriodLengthStep);
                }

                var periodLength = MessageText.ExtractFirstInteger(body);
                if (periodLength is < 2 or > 10 or null)
                {
                    var periodLengthStepOutOfOrderReply = await TryCaptureOutOfOrderIntentAsync(user, body, rawBody, today);
                    if (periodLengthStepOutOfOrderReply is not null)
                    {
                        return periodLengthStepOutOfOrderReply;
                    }

                    return MisunderstoodMessage("Me diga um numero entre 2 e 10 dias para a duracao media da menstruacao.");
                }

                EnsurePreference(user).AveragePeriodLength = periodLength.Value;
                user.OnboardingStep = OnboardingSteps.AwaitingContraceptiveMethod;
                return NextOnboardingPrompt(user, new OnboardingExtraction { AveragePeriodLength = periodLength.Value });

            case OnboardingSteps.AwaitingContraceptiveMethod:
                var extractedFromContraceptiveStep = await ExtractOnboardingDataAsync(body, rawBody, today);
                var contraceptive = extractedFromContraceptiveStep.ContraceptiveType ?? ParseContraceptiveType(body);
                if (contraceptive is null)
                {
                    var contraceptiveStepOutOfOrderReply = await TryCaptureOutOfOrderIntentAsync(user, body, rawBody, today);
                    if (contraceptiveStepOutOfOrderReply is not null)
                    {
                        return contraceptiveStepOutOfOrderReply;
                    }

                    return MisunderstoodMessage("Pode responder algo como \"tomo pilula\", \"uso DIU\", \"uso camisinha\", \"nao uso\" ou \"prefiro nao informar\".");
                }

                ApplyContraceptivePreference(user, contraceptive);
                user.OnboardingStep = OnboardingSteps.Completed;
                return await CompleteOnboardingWithPendingPromptAsync(user, new OnboardingExtraction { ContraceptiveType = contraceptive });

            default:
                user.OnboardingStep = OnboardingSteps.AwaitingConsent;
                return InitialConsentMessage();
        }
    }

    private async Task<OnboardingExtraction> ExtractOnboardingDataAsync(string body, string rawBody, DateOnly today)
    {
        var extraction = ExtractDeterministicOnboardingData(body, rawBody, today);
        if (ShouldUseAiForOnboarding(rawBody))
        {
            var aiExtraction = await onboardingAi.ExtractAsync(rawBody, today);
            MergeOnboardingExtraction(extraction, aiExtraction);
        }

        RemoveUnsafeInferences(extraction, body);
        return extraction;
    }

    private async Task ApplyExtractedOnboardingDataAsync(LumaUser user, OnboardingExtraction extraction)
    {
        var moved = true;
        while (moved)
        {
            moved = false;

            if (user.OnboardingStep == OnboardingSteps.AwaitingDisplayName && extraction.DisplayName is not null)
            {
                user.DisplayName = extraction.DisplayName;
                user.OnboardingStep = OnboardingSteps.AwaitingAgeConfirmation;
                moved = true;
            }

            if (user.OnboardingStep == OnboardingSteps.AwaitingAgeConfirmation && extraction.IsAdultConfirmed is not null)
            {
                user.IsAdultConfirmed = extraction.IsAdultConfirmed.Value;
                user.OnboardingStep = extraction.IsAdultConfirmed.Value
                    ? OnboardingSteps.AwaitingLastPeriodStart
                    : OnboardingSteps.UnderageBlocked;
                moved = true;
            }

            if (user.OnboardingStep == OnboardingSteps.AwaitingLastPeriodStart)
            {
                if (extraction.LastPeriodUnknown)
                {
                    EnsurePreference(user).LastPeriodStartDate = null;
                    user.OnboardingStep = OnboardingSteps.AwaitingAverageCycleLength;
                    moved = true;
                }
                else if (extraction.LastPeriodStartDate is not null)
                {
                    EnsurePreference(user).LastPeriodStartDate = extraction.LastPeriodStartDate.Value;
                    var onboardingCycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, extraction.LastPeriodStartDate.Value, CycleStatus.Unknown);
                    await AddCycleEventAsync(user.Id, onboardingCycle.Id, CycleEventTypes.PeriodStart, extraction.LastPeriodStartDate.Value, new { onboarding = true });
                    user.OnboardingStep = OnboardingSteps.AwaitingAverageCycleLength;
                    moved = true;
                }
            }

            if (user.OnboardingStep == OnboardingSteps.AwaitingAverageCycleLength && extraction.AverageCycleLength is not null)
            {
                EnsurePreference(user).AverageCycleLength = extraction.AverageCycleLength.Value;
                user.OnboardingStep = OnboardingSteps.AwaitingAveragePeriodLength;
                moved = true;
            }

            if (user.OnboardingStep == OnboardingSteps.AwaitingAveragePeriodLength && extraction.AveragePeriodLength is not null)
            {
                EnsurePreference(user).AveragePeriodLength = extraction.AveragePeriodLength.Value;
                user.OnboardingStep = OnboardingSteps.AwaitingContraceptiveMethod;
                moved = true;
            }

            if (user.OnboardingStep == OnboardingSteps.AwaitingContraceptiveMethod && extraction.ContraceptiveType is not null)
            {
                ApplyContraceptivePreference(user, extraction.ContraceptiveType);
                user.OnboardingStep = OnboardingSteps.Completed;
                moved = true;
            }
        }
    }

    private static OnboardingExtraction ExtractDeterministicOnboardingData(string body, string rawBody, DateOnly today)
    {
        var extraction = new OnboardingExtraction();

        var name = TryExtractDisplayName(body, rawBody);
        if (name is not null)
        {
            extraction.DisplayName = name;
        }

        var age = TryExtractAge(body);
        if (age is not null)
        {
            extraction.IsAdultConfirmed = age >= 18;
        }

        if (DateParser.IsUnknown(body))
        {
            extraction.LastPeriodUnknown = true;
        }

        var date = DateParser.ParseFlexibleDate(rawBody, today);
        if (date is not null)
        {
            extraction.LastPeriodStartDate = date.Value;
        }

        var daysAgo = DateParser.ParseDaysAgo(rawBody);
        if (daysAgo is not null)
        {
            extraction.LastPeriodDaysAgo = daysAgo;
            extraction.LastPeriodStartDate = today.AddDays(-daysAgo.Value);
        }

        var firstInteger = MessageText.ExtractFirstInteger(body);
        if (body.Contains("ciclo", StringComparison.Ordinal) && firstInteger is >= 21 and <= 45)
        {
            extraction.AverageCycleLength = firstInteger;
        }

        if ((body.Contains("dura", StringComparison.Ordinal) || body.Contains("menstruacao", StringComparison.Ordinal))
            && firstInteger is >= 2 and <= 10)
        {
            extraction.AveragePeriodLength = firstInteger;
        }

        extraction.ContraceptiveType = ParseContraceptiveType(body);

        return extraction;
    }

    private static void MergeOnboardingExtraction(OnboardingExtraction target, OnboardingExtraction? source)
    {
        if (source is null)
        {
            return;
        }

        target.DisplayName ??= source.DisplayName;
        target.IsAdultConfirmed ??= source.IsAdultConfirmed;
        target.LastPeriodDaysAgo ??= source.LastPeriodDaysAgo;
        target.LastPeriodStartDate ??= source.LastPeriodStartDate;
        target.LastPeriodUnknown = target.LastPeriodUnknown || source.LastPeriodUnknown;
        target.AverageCycleLength ??= source.AverageCycleLength;
        target.AveragePeriodLength ??= source.AveragePeriodLength;
        target.ContraceptiveType ??= source.ContraceptiveType;
    }

    private static void RemoveUnsafeInferences(OnboardingExtraction extraction, string body)
    {
        if (extraction.IsAdultConfirmed is not null && !HasExplicitAgeEvidence(body))
        {
            extraction.IsAdultConfirmed = null;
        }

        if (extraction.LastPeriodUnknown && !DateParser.IsUnknown(body))
        {
            extraction.LastPeriodUnknown = false;
        }

        if (extraction.LastPeriodStartDate is not null)
        {
            extraction.LastPeriodUnknown = false;
        }
    }

    private async Task<ConversationIntent> ExtractConversationIntentAsync(string body, string rawBody, DateOnly today, LumaUser? user = null)
    {
        var deterministic = ExtractDeterministicConversationIntent(body, rawBody, today);
        if (ShouldUseAiForOnboarding(rawBody))
        {
            var ai = await conversationAi.ExtractAsync(rawBody, today, user is null ? null : BuildConversationContext(user));
            MergeConversationIntent(deterministic, ai);
        }

        return deterministic;
    }

    private async Task<string?> TryCaptureOutOfOrderIntentAsync(LumaUser user, string body, string rawBody, DateOnly today)
    {
        if (user.OnboardingStep is OnboardingSteps.AwaitingConsent
            or OnboardingSteps.ConsentDeclined
            or OnboardingSteps.UnderageBlocked
            or OnboardingSteps.Completed)
        {
            return null;
        }

        var intent = ExtractDeterministicConversationIntent(body, rawBody, today);
        if (!IsActionablePendingIntent(intent.Intent) && ShouldAskAiForOutOfOrderIntent(body, rawBody))
        {
            var ai = await conversationAi.ExtractAsync(rawBody, today, BuildConversationContext(user));
            MergeConversationIntent(intent, ai);
        }

        if (!IsActionablePendingIntent(intent.Intent) || !IsOutOfOrderForCurrentStep(user.OnboardingStep, intent.Intent))
        {
            return null;
        }

        var pending = await SavePendingIntentAsync(user.Id, intent, rawBody, today);
        return PendingIntentCapturedMessage(user, pending, today);
    }

    private static ConversationIntent ExtractDeterministicConversationIntent(string body, string rawBody, DateOnly today)
    {
        var intent = new ConversationIntent();

        if (IsLumaIdentityQuestion(body))
        {
            intent.Intent = ConversationIntents.LumaIdentityQuestion;
            return intent;
        }

        if (LumaKnowledgeBase.IsKnowledgeQuestion(body))
        {
            intent.Intent = ConversationIntents.KnowledgeQuestion;
            return intent;
        }

        if (IsLastSexualActivityQuestion(body))
        {
            intent.Intent = ConversationIntents.LastSexualActivityQuestion;
            return intent;
        }

        if (IsPregnancyWeeksQuestion(body))
        {
            intent.Intent = ConversationIntents.PregnancyWeeksQuestion;
            return intent;
        }

        if (IsPregnancyDueDateQuestion(body))
        {
            intent.Intent = ConversationIntents.PregnancyDueDateQuestion;
            return intent;
        }

        if (IsPeriodStart(body))
        {
            intent.Intent = ConversationIntents.PeriodStart;
            intent.Date = InferPeriodStartDate(body, rawBody, today);
            return intent;
        }

        if (IsPeriodEnd(body))
        {
            intent.Intent = ConversationIntents.PeriodEnd;
            intent.Date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            return intent;
        }

        if (IsPregnancyPositiveStatement(body))
        {
            intent.Intent = ConversationIntents.PregnancyPositive;
            intent.Date = today;
            intent.GestationalWeeks = ParseGestationalWeeks(body);
            intent.LastPeriodDate = DateParser.ParseFlexibleDate(rawBody, today);
            if (intent.GestationalWeeks is not null)
            {
                intent.LastPeriodDate = today.AddDays(-(intent.GestationalWeeks.Value * 7));
            }

            intent.EstimatedDueDate = intent.LastPeriodDate?.AddDays(280);
            return intent;
        }

        if (IsPregnancyBleeding(body))
        {
            intent.Intent = ConversationIntents.PregnancyBleeding;
            intent.Date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            return intent;
        }

        if (IsPrenatalAppointment(body))
        {
            intent.Intent = ConversationIntents.PrenatalAppointment;
            intent.Date = ParseUpcomingDate(rawBody, today) ?? today;
            return intent;
        }

        if (IsUltrasound(body))
        {
            intent.Intent = ConversationIntents.Ultrasound;
            intent.Date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            return intent;
        }

        if (IsPregnancySymptom(body))
        {
            intent.Intent = ConversationIntents.PregnancySymptom;
            intent.Date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            var symptom = ParseSymptoms(body).FirstOrDefault();
            intent.Symptom = string.IsNullOrWhiteSpace(symptom.Key) ? "symptom" : symptom.Key;
            intent.Intensity = ParseSymptomIntensity(body);
            return intent;
        }

        var symptoms = ParseSymptoms(body);
        if (symptoms.Count > 0)
        {
            intent.Intent = ConversationIntents.Symptom;
            intent.Date = today;
            intent.Symptom = symptoms.First().Key;
            intent.Intensity = symptoms.First().Intensity;
            return intent;
        }

        var flow = ParseFlowIntensity(body);
        if (flow is not null && IsFlowOnlyResponse(body))
        {
            intent.Intent = ConversationIntents.FlowUpdate;
            intent.Date = today;
            intent.Intensity = flow;
            return intent;
        }

        var mood = ParseMood(body);
        if (mood is not null)
        {
            intent.Intent = ConversationIntents.Mood;
            intent.Date = today;
            intent.Symptom = mood.Value.Key;
            return intent;
        }

        if (IsSexualActivity(body))
        {
            intent.Intent = ConversationIntents.SexualActivity;
            intent.Date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            intent.Protected = ParseProtectedValue(body);
            return intent;
        }

        if (LooksLikeQuestion(body) && !LooksLikeKnownCompletedIntent(body, new ConversationIntent()))
        {
            intent.Intent = ConversationIntents.OutOfScope;
        }

        return intent;
    }

    private static void MergeConversationIntent(ConversationIntent target, ConversationIntent? source)
    {
        if (source is null || source.Confidence is < 0.55)
        {
            return;
        }

        target.Intent ??= source.Intent;
        target.Date ??= source.Date;
        target.GestationalWeeks ??= source.GestationalWeeks;
        target.LastPeriodDate ??= source.LastPeriodDate;
        target.EstimatedDueDate ??= source.EstimatedDueDate;
        target.Protected ??= source.Protected;
        target.Symptom ??= source.Symptom;
        target.Intensity ??= source.Intensity;
        target.Confidence ??= source.Confidence;
    }

    private async Task<string> HandleCompletedUserMessageAsync(LumaUser user, string body, string rawBody, ConversationIntent intent)
    {
        var today = Today();

        if (user.PendingAction == PendingActions.AwaitingPregnancyReference)
        {
            return await HandlePregnancyReferenceAsync(user, body, rawBody, intent, today);
        }

        var pendingIntentReply = await HandlePendingIntentConfirmationAsync(user, body, today);
        if (pendingIntentReply is not null)
        {
            return pendingIntentReply;
        }

        if (intent.Intent == ConversationIntents.LumaIdentityQuestion)
        {
            return LumaIdentityMessage();
        }

        if (intent.Intent == ConversationIntents.KnowledgeQuestion && LumaKnowledgeBase.Search(body) is { } knowledgeReply)
        {
            return knowledgeReply;
        }

        if (intent.Intent == ConversationIntents.OutOfScope)
        {
            return OutOfScopeMessage();
        }

        if (IsHelp(body))
        {
            return HelpMessage();
        }

        if (IsGreeting(body))
        {
            return GreetingMessage(user);
        }

        if (body.Contains("apagar", StringComparison.Ordinal) || body.Contains("excluir", StringComparison.Ordinal))
        {
            return "Eu posso apagar seus dados, mas essa acao precisa de confirmacao. Para este MVP local, me avise no painel/admin ou implemente o fluxo definitivo antes de usar em producao.";
        }

        if (intent.Intent == ConversationIntents.LastSexualActivityQuestion)
        {
            return await BuildLastSexualActivityReplyAsync(user.Id);
        }

        if (intent.Intent == ConversationIntents.PregnancyWeeksQuestion)
        {
            return await BuildPregnancyWeeksReplyAsync(user.Id, today);
        }

        if (intent.Intent == ConversationIntents.PregnancyDueDateQuestion)
        {
            return await BuildPregnancyDueDateReplyAsync(user.Id);
        }

        if (intent.Intent == ConversationIntents.PregnancyPositive)
        {
            return await HandlePregnancyPositiveAsync(user, intent, today);
        }

        var activePregnancy = await GetActivePregnancyAsync(user.Id);
        if (activePregnancy is not null)
        {
            if (intent.Intent == ConversationIntents.PregnancyBleeding)
            {
                return await HandlePregnancyBleedingAsync(user.Id, intent.Date ?? DateParser.ParseFlexibleDate(rawBody, today) ?? today);
            }

            if (intent.Intent == ConversationIntents.PrenatalAppointment)
            {
                var date = intent.Date ?? ParseUpcomingDate(rawBody, today) ?? today;
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.PrenatalAppointment, date, new { pregnancy_id = activePregnancy.Id });
                return $"Registrei sua consulta de pre-natal em {FormatDate(date)}. Leve esse historico como apoio, mas mantenha sempre o acompanhamento com seu medico ou obstetra.";
            }

            if (intent.Intent == ConversationIntents.Ultrasound)
            {
                var date = intent.Date ?? DateParser.ParseFlexibleDate(rawBody, today) ?? today;
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.Ultrasound, date, new { pregnancy_id = activePregnancy.Id });
                return $"Registrei o ultrassom em {FormatDate(date)}. Fico feliz em te ajudar a organizar esse acompanhamento por aqui.";
            }

            if (intent.Intent == ConversationIntents.PregnancySymptom)
            {
                var symptom = intent.Symptom ?? ParseSymptoms(body).FirstOrDefault().Key ?? "symptom";
                var intensity = intent.Intensity ?? ParseSymptomIntensity(body);
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.PregnancySymptom, intent.Date ?? today, new
                {
                    pregnancy_id = activePregnancy.Id,
                    symptom,
                    intensity
                });

                return "Registrei esse sintoma na sua gravidez. Se vier com dor forte, febre, tontura, sangramento, perda de liquido ou mal-estar importante, fale com seu medico ou obstetra.";
            }
        }

        if (IsLastSymptomQuestion(body))
        {
            return await BuildLastSymptomReplyAsync(user.Id);
        }

        if (IsLastPeriodQuestion(body))
        {
            return BuildLastPeriodReply(user);
        }

        if (IsDelayQuestion(body))
        {
            return BuildDelayReply(user, today);
        }

        if (IsNextPeriodQuestion(body))
        {
            return BuildNextPeriodReply(user);
        }

        if (intent.Intent == ConversationIntents.PeriodStart || IsPeriodStart(body))
        {
            var date = intent.Date ?? InferPeriodStartDate(body, rawBody, today);
            var periodFlow = ParseFlowIntensity(body);
            var cycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, date);
            EnsurePreference(user).LastPeriodStartDate = date;

            await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodStart, date, new { });

            if (periodFlow is not null)
            {
                await AddOrReplaceFlowEventAsync(user.Id, cycle.Id, date, periodFlow);
                return $"Registrei o inicio da sua menstruacao em {FormatDate(date)} com fluxo {FlowLabel(periodFlow)}. Vou considerar esse como o inicio do seu ciclo atual.";
            }

            user.PendingAction = PendingActions.AwaitingFlowIntensity;
            return $"Registrei o inicio da sua menstruacao em {FormatDate(date)}.\n\nComo esta o fluxo?\n1. Leve\n2. Medio\n3. Intenso\n4. Prefiro nao informar";
        }

        if (intent.Intent == ConversationIntents.PeriodEnd || IsPeriodEnd(body))
        {
            var date = intent.Date ?? DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            var cycle = await db.Cycles
                .Where(existing => existing.UserId == user.Id && existing.Status == CycleStatus.Ongoing)
                .OrderByDescending(existing => existing.StartDate)
                .FirstOrDefaultAsync();

            if (cycle is null)
            {
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.PeriodEnd, date, new { needs_period_start = true });
                return $"Registrei que sua menstruacao terminou em {FormatDate(date)}. Ainda nao encontrei um ciclo aberto para calcular a duracao; se puder, me diga quando ela comecou.";
            }

            if (date < cycle.StartDate)
            {
                return $"A data de termino ficou antes do inicio do ciclo ({FormatDate(cycle.StartDate)}). Pode me mandar a data de termino de novo?";
            }

            cycle.EndDate = date;
            cycle.Status = CycleStatus.Finished;
            cycle.UpdatedAt = DateTimeOffset.UtcNow;
            await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodEnd, date, new { });

            var days = Math.Max(1, date.DayNumber - cycle.StartDate.DayNumber + 1);
            var nextPeriod = cycle.StartDate.AddDays(EnsurePreference(user).AverageCycleLength);
            return $"Registrei que sua menstruacao terminou em {FormatDate(date)}. Ela durou cerca de {days} dias neste ciclo. Pela sua media atual, a proxima menstruacao esta prevista para perto de {FormatDate(nextPeriod)}.";
        }

        var symptoms = ParseSymptoms(body);
        if (symptoms.Count > 0)
        {
            foreach (var symptom in symptoms)
            {
                await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.Symptom, today, new
                {
                    symptom = symptom.Key,
                    intensity = symptom.Intensity
                });
            }

            var labels = string.Join(" e ", symptoms.Select(symptom => symptom.Label));
            return $"Registrei {labels} para hoje. Obrigada por me contar. Se vier com dor muito forte, sangramento intenso, febre, tontura ou mal-estar importante, procure orientacao medica.";
        }

        var flow = ParseFlowIntensity(body);
        if (flow is not null)
        {
            var updated = await HasCycleEventAsync(user.Id, CycleEventTypes.FlowUpdate, today);
            await AddOrReplaceFlowEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), today, flow);
            if (flow == "unknown")
            {
                return "Tudo bem, deixei o fluxo de hoje sem informar.";
            }

            return updated
                ? $"Atualizei aqui. Hoje ficou registrado como fluxo {FlowLabel(flow)}."
                : $"Registrei fluxo {FlowLabel(flow)} para hoje.";
        }

        var mood = ParseMood(body);
        if (mood is not null)
        {
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.Mood, today, new
            {
                mood = mood.Value.Key
            });

            return $"Registrei que voce esta se sentindo {mood.Value.Label} hoje. Isso fica guardado so como historico para te ajudar a perceber padroes ao longo dos ciclos.";
        }

        if (intent.Intent == ConversationIntents.SexualActivity || IsSexualActivity(body))
        {
            var date = intent.Date ?? DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            var metadata = BuildSexualActivityMetadata(body, intent);
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.SexualActivity, date, metadata);
            return $"Registrei a relacao em {FormatDate(date)}. Esse dado fica salvo apenas para seu historico; eu nao uso isso para afirmar gravidez ou diagnostico.";
        }

        if (LooksLikeQuestion(body))
        {
            return OutOfScopeMessage();
        }

        return MisunderstoodMessage("Voce pode tentar de um jeito mais direto, como \"menstruei hoje\", \"acabou ontem\", \"fluxo intenso\", \"to com colica forte\", \"tive relacao ontem\", \"estou gravida de 8 semanas\" ou \"quando e minha proxima menstruacao?\".");
    }

    private async Task<Cycle> CreateOrUpdateCycleFromLastPeriodAsync(Guid userId, DateOnly date, string status = CycleStatus.Ongoing)
    {
        var existing = await db.Cycles.FirstOrDefaultAsync(cycle => cycle.UserId == userId && cycle.StartDate == date);

        if (existing is not null)
        {
            existing.Status = status;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            return existing;
        }

        var cycleNumber = await db.Cycles.CountAsync(cycle => cycle.UserId == userId) + 1;
        var cycle = new Cycle
        {
            UserId = userId,
            StartDate = date,
            Status = status,
            CycleNumber = cycleNumber
        };
        db.Cycles.Add(cycle);
        return cycle;
    }

    private DateOnly Today()
    {
        return DateOnly.FromDateTime(dateProvider.UtcNow.UtcDateTime);
    }

    private async Task<Guid?> GetCurrentCycleIdAsync(Guid userId)
    {
        return await db.Cycles
            .Where(cycle => cycle.UserId == userId && cycle.Status == CycleStatus.Ongoing)
            .OrderByDescending(cycle => cycle.StartDate)
            .Select(cycle => (Guid?)cycle.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> HasCycleEventAsync(Guid userId, string type, DateOnly date)
    {
        return await db.CycleEvents.AnyAsync(ev => ev.UserId == userId && ev.Type == type && ev.Date == date);
    }

    private async Task AddOrReplaceFlowEventAsync(Guid userId, Guid? cycleId, DateOnly date, string flow)
    {
        var existing = await db.CycleEvents
            .Where(ev => ev.UserId == userId && ev.Type == CycleEventTypes.FlowUpdate && ev.Date == date)
            .ToListAsync();

        db.CycleEvents.RemoveRange(existing);
        await AddCycleEventAsync(userId, cycleId, CycleEventTypes.FlowUpdate, date, new { flow_intensity = flow });
    }

    private Task AddCycleEventAsync(Guid userId, Guid? cycleId, string type, DateOnly date, object metadata)
    {
        db.CycleEvents.Add(new CycleEvent
        {
            UserId = userId,
            CycleId = cycleId,
            Type = type,
            Date = date,
            Source = "whatsapp",
            MetadataJson = JsonSerializer.Serialize(metadata)
        });

        return Task.CompletedTask;
    }

    private async Task<PendingIntent> SavePendingIntentAsync(Guid userId, ConversationIntent intent, string rawBody, DateOnly today)
    {
        var existing = await db.PendingIntents
            .Where(pending => pending.UserId == userId && pending.Status == PendingIntentStatus.PendingConfirmation)
            .ToListAsync();

        foreach (var pending in existing)
        {
            pending.Status = PendingIntentStatus.Dismissed;
            pending.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var pendingIntent = new PendingIntent
        {
            UserId = userId,
            Intent = intent.Intent!,
            Date = intent.Date ?? today,
            RequiredBeforeAction = PendingIntentRequirements.FinishOnboarding,
            Status = PendingIntentStatus.PendingConfirmation,
            PayloadJson = JsonSerializer.Serialize(new
            {
                original_message = _storeMessageBodies ? rawBody : null,
                @protected = intent.Protected,
                symptom = intent.Symptom,
                intensity = intent.Intensity,
                gestational_weeks = intent.GestationalWeeks,
                last_period_date = intent.LastPeriodDate,
                estimated_due_date = intent.EstimatedDueDate
            })
        };

        db.PendingIntents.Add(pendingIntent);
        return pendingIntent;
    }

    private async Task<PendingIntent?> GetLatestPendingIntentAsync(Guid userId)
    {
        return await db.PendingIntents
            .Where(intent => intent.UserId == userId && intent.Status == PendingIntentStatus.PendingConfirmation)
            .OrderByDescending(intent => intent.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task<string> CompleteOnboardingWithPendingPromptAsync(LumaUser user, OnboardingExtraction? captured)
    {
        var reply = NextOnboardingPrompt(user, captured);
        var pending = await GetLatestPendingIntentAsync(user.Id);
        if (pending is null)
        {
            return reply;
        }

        return $"{reply}\n\n{PendingIntentConfirmationPrompt(pending, Today())}";
    }

    private async Task<string?> HandlePendingIntentConfirmationAsync(LumaUser user, string body, DateOnly today)
    {
        var pending = await GetLatestPendingIntentAsync(user.Id);
        if (pending is null)
        {
            return null;
        }

        if (IsNegative(body))
        {
            pending.Status = PendingIntentStatus.Dismissed;
            pending.CompletedAt = DateTimeOffset.UtcNow;
            pending.UpdatedAt = DateTimeOffset.UtcNow;
            return "Tudo bem, nao registrei aquela informacao. Quando quiser, pode me mandar de novo de um jeito natural.";
        }

        if (!IsAffirmative(body))
        {
            return null;
        }

        var reply = await ExecutePendingIntentAsync(user, pending, today);
        pending.Status = PendingIntentStatus.Completed;
        pending.CompletedAt = DateTimeOffset.UtcNow;
        pending.UpdatedAt = DateTimeOffset.UtcNow;
        return reply;
    }

    private async Task<string> ExecutePendingIntentAsync(LumaUser user, PendingIntent pending, DateOnly today)
    {
        var date = pending.Date ?? today;

        if (pending.Intent == ConversationIntents.PeriodStart)
        {
            var cycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, date);
            EnsurePreference(user).LastPeriodStartDate = date;
            await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodStart, date, new { pending_intent = true });
            return $"Registrei o inicio da sua menstruacao em {FormatDate(date)}. Obrigada por confirmar; deixei isso organizado no seu historico.";
        }

        if (pending.Intent == ConversationIntents.SexualActivity)
        {
            var protectedValue = ExtractPendingString(pending.PayloadJson, "protected") ?? "unknown";
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.SexualActivity, date, new
            {
                pending_intent = true,
                protected_value = protectedValue,
                @protected = protectedValue,
                contraceptive_method = "unknown"
            });

            return $"Registrei a relacao em {FormatDate(date)}. Esse dado fica salvo apenas para seu historico; eu nao uso isso para afirmar gravidez ou diagnostico.";
        }

        if (pending.Intent == ConversationIntents.PregnancyPositive)
        {
            return await HandlePregnancyPositiveAsync(user, new ConversationIntent { Intent = ConversationIntents.PregnancyPositive, Date = date }, today);
        }

        pending.Status = PendingIntentStatus.Dismissed;
        pending.CompletedAt = DateTimeOffset.UtcNow;
        pending.UpdatedAt = DateTimeOffset.UtcNow;
        return "Eu tinha guardado uma intencao anterior, mas ainda nao consigo executar esse tipo de registro com seguranca. Pode me mandar de novo depois do cadastro, de forma direta?";
    }

    private static ConsentRecord NewConsent(Guid userId, string type)
    {
        return new ConsentRecord
        {
            UserId = userId,
            ConsentType = type,
            Accepted = true
        };
    }

    private static UserPreference EnsurePreference(LumaUser user)
    {
        user.Preference ??= new UserPreference { UserId = user.Id };
        return user.Preference;
    }

    private static ConversationContext BuildConversationContext(LumaUser user)
    {
        return new ConversationContext
        {
            DisplayName = user.DisplayName,
            OnboardingStep = user.OnboardingStep,
            PendingAction = user.PendingAction,
            HasAcceptedConsent = user.ConsentAcceptedAt is not null,
            HasCompletedOnboarding = user.OnboardingStep == OnboardingSteps.Completed
        };
    }

    private static void ApplyContraceptivePreference(LumaUser user, string contraceptiveType)
    {
        var preference = EnsurePreference(user);
        preference.ContraceptiveType = contraceptiveType;
        preference.UsesHormonalContraceptive = contraceptiveType is "pill" or "injection" or "hormonal_iud" or "implant";
        preference.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private Task<string> AcceptConsentAsync(LumaUser user)
    {
        user.ConsentAcceptedAt = DateTimeOffset.UtcNow;
        user.OnboardingStep = OnboardingSteps.AwaitingDisplayName;

        db.Consents.AddRange(
            NewConsent(user.Id, "privacy_policy"),
            NewConsent(user.Id, "terms_of_use"),
            NewConsent(user.Id, "health_data_processing"),
            NewConsent(user.Id, "whatsapp_contact"));

        return Task.FromResult("Obrigada por confiar em mim. Para eu deixar nossa conversa mais pessoal, como devo te chamar?");
    }

    private static string InitialConsentMessage()
    {
        return "Oi! Eu sou sua assistente de ciclo pelo WhatsApp.\n\nAntes de comecar: eu posso te ajudar a registrar menstruacao, sintomas, lembretes e historico. Nao substituo orientacao medica e nao faco diagnosticos.\n\nPara continuar, preciso do seu consentimento para armazenar dados relacionados ao seu ciclo, sintomas e saude menstrual.\n\nVoce aceita?\n1. Aceito\n2. Nao aceito";
    }

    private static string MisunderstoodMessage(string hint)
    {
        return $"Não entendi sua resposta. Poderia tentar de novo, talvez de uma maneira mais direta?\n\n{hint}";
    }

    private static string NextOnboardingPrompt(LumaUser user, OnboardingExtraction? captured = null)
    {
        var firstContact = captured?.DisplayName is not null;
        var manyDetails = CountCapturedFields(captured) >= 2;
        var prefix = string.IsNullOrWhiteSpace(user.DisplayName) ? "" : $"{user.DisplayName}, ";

        return user.OnboardingStep switch
        {
            OnboardingSteps.AwaitingDisplayName => "Para eu deixar nossa conversa mais pessoal, como devo te chamar? Pode mandar so seu primeiro nome ou apelido.",
            OnboardingSteps.AwaitingAgeConfirmation => firstContact
                ? $"Ola, {user.DisplayName}, prazer em conhece-la. Meu nome e Luma e vou ser sua assistente de ciclo por aqui.\n\nAntes de seguirmos, voce poderia me confirmar se tem 18 anos ou mais?\n1. Sim\n2. Nao"
                : $"{prefix}antes de continuar, voce poderia me confirmar se tem 18 anos ou mais?\n1. Sim\n2. Nao",
            OnboardingSteps.AwaitingLastPeriodStart => manyDetails
                ? $"{prefix}obrigada por ja me passar essas informacoes. Agora me diz: qual foi o primeiro dia da sua ultima menstruacao?\n\nPode responder tipo \"comecou dia 10/04\" ou \"nao lembro\"."
                : $"{prefix}obrigada por confirmar. Qual foi o primeiro dia da sua ultima menstruacao?\n\nPode responder tipo \"comecou dia 10/04\" ou \"nao lembro\".",
            OnboardingSteps.AwaitingAverageCycleLength => manyDetails
                ? $"Prazer em conhece-la, {user.DisplayName}. Obrigada por ja me passar essas informacoes. Para finalizar seu cadastro, me diz: seu ciclo costuma ter quantos dias?\n\nSe nao souber, posso comecar usando 28 dias e ir ajustando com o tempo."
                : $"{prefix}perfeito, obrigada. Seu ciclo costuma ter quantos dias?\n\nSe nao souber, posso comecar usando 28 dias e ir ajustando com o tempo.",
            OnboardingSteps.AwaitingAveragePeriodLength => manyDetails
                ? $"{prefix}otimo, ja deixei esses dados iniciais organizados. So falta uma coisinha: sua menstruacao costuma durar quantos dias?"
                : $"{prefix}entendi. Sua menstruacao costuma durar quantos dias?",
            OnboardingSteps.AwaitingContraceptiveMethod => $"{prefix}para deixar seu cadastro mais completo, voce usa algum metodo contraceptivo? Pode responder, se quiser, algo como:\n\n1. Nao uso\n2. Pilula\n3. Injecao\n4. DIU hormonal\n5. DIU de cobre\n6. Implante\n7. Camisinha\n8. Outro\n9. Prefiro nao informar",
            OnboardingSteps.Completed => CompletedOnboardingMessage(user, captured),
            OnboardingSteps.UnderageBlocked => "Obrigada por responder. Por seguranca, a Luma ainda nao pode continuar esse cadastro pelo WhatsApp para menores de 18 anos.",
            _ => InitialConsentMessage()
        };
    }

    private static string CompletedOnboardingMessage(LumaUser user, OnboardingExtraction? captured = null)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "" : $"{user.DisplayName}, ";
        var intro = CountCapturedFields(captured) >= 2
            ? $"{name}pronto. Obrigada por ja me passar tudo isso; seu cadastro inicial ficou completo."
            : $"{name}pronto. Seu cadastro inicial ficou completo.";

        return $"{intro}\n\nA partir de agora, pode falar comigo de um jeito bem natural. Por exemplo:\n\n\"menstruei hoje\"\n\"acabou ontem\"\n\"fluxo intenso\"\n\"to com colica forte\"\n\"hoje estou irritada\"\n\"tive relacao dia 20\"\n\"quando e minha proxima menstruacao?\"\n\nEu vou te ajudar a organizar seus registros, sempre como estimativa e sem substituir orientacao medica.";
    }

    private static string PendingIntentCapturedMessage(LumaUser user, PendingIntent pending, DateOnly today)
    {
        var dateLabel = FormatRelativeDateForReply(pending.Date ?? today, today);
        var nextPrompt = NextOnboardingPrompt(user);

        var understood = pending.Intent switch
        {
            ConversationIntents.PeriodStart => $"Entendi, ja vi que voce quer registrar o inicio da menstruacao {dateLabel}.",
            ConversationIntents.SexualActivity => $"Entendi, ja vi que voce quer registrar uma relacao {dateLabel}.",
            ConversationIntents.PregnancyPositive => "Entendi, ja vi que voce quer me contar sobre uma gravidez.",
            _ => "Entendi o que voce quer registrar."
        };

        return $"{understood} Antes disso, preciso terminar seu cadastro rapidinho para salvar tudo do jeito certo e com seguranca.\n\n{nextPrompt}";
    }

    private static string PendingIntentConfirmationPrompt(PendingIntent pending, DateOnly today)
    {
        var dateLabel = FormatRelativeDateForReply(pending.Date ?? today, today);
        return pending.Intent switch
        {
            ConversationIntents.PeriodStart => $"Voce tinha me contado que sua menstruacao comecou {dateLabel}. Quer que eu registre isso agora?\n1. Sim\n2. Nao",
            ConversationIntents.SexualActivity => $"Voce tinha me contado que teve uma relacao {dateLabel}. Quer que eu registre isso agora?\n1. Sim\n2. Nao",
            ConversationIntents.PregnancyPositive => "Voce tinha me contado sobre uma gravidez. Quer que eu registre isso agora?\n1. Sim\n2. Nao",
            _ => "Voce tinha me contado algo antes de terminar o cadastro. Quer que eu registre isso agora?\n1. Sim\n2. Nao"
        };
    }

    private static string HelpMessage()
    {
        return "Voce pode me mandar frases simples como:\n\n\"menstruei hoje\"\n\"acabou ontem\"\n\"fluxo leve\"\n\"to com colica forte\"\n\"hoje estou ansiosa\"\n\"tive relacao dia 20\"\n\"minha menstruacao esta atrasada?\"\n\"quando e minha proxima menstruacao?\"";
    }

    private static string LumaIdentityMessage()
    {
        return "Eu sou a Luma, uma IA de apoio para ciclo menstrual e gravidez pelo WhatsApp. Meu intuito e te ajudar a registrar menstruacao, sintomas, humor, relacoes, historico e dados de acompanhamento da gravidez de um jeito simples e acolhedor.\n\nEu nao faco diagnosticos, nao confirmo gravidez, nao digo se sangramentos sao normais e nao substituo orientacao medica. Quando algo puder envolver risco, vou te orientar a procurar um profissional de saude.";
    }

    private static string OutOfScopeMessage()
    {
        return "Desculpa, nao posso opinar sobre isso. Eu consigo te ajudar com registros e duvidas seguras sobre ciclo menstrual, menstruacao, sintomas, historico, relacoes registradas, gravidez e funcionamento da Luma, sempre sem fazer diagnosticos.";
    }

    private static string GreetingMessage(LumaUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "" : $", {user.DisplayName}";
        return $"Oi{name}. Estou por aqui.\n\nPode falar comigo de forma natural, como \"menstruei hoje\", \"acabou ontem\", \"to com colica forte\" ou \"quando e minha proxima menstruacao?\".";
    }

    private static string BuildDelayReply(LumaUser user, DateOnly today)
    {
        var preference = EnsurePreference(user);
        if (preference.LastPeriodStartDate is null)
        {
            return "Ainda nao tenho a data da sua ultima menstruacao para calcular atraso. Voce pode me dizer algo como \"menstruei dia 10/04\".";
        }

        var expected = preference.LastPeriodStartDate.Value.AddDays(preference.AverageCycleLength);
        var delayDays = today.DayNumber - expected.DayNumber;

        if (delayDays <= 0)
        {
            return $"Pela sua previsao atual, ainda nao parece atrasada. Sua proxima menstruacao esta prevista para perto de {FormatDate(expected)}. Isso e so uma estimativa baseada nos seus registros.";
        }

        return $"Pela sua previsao atual, sua menstruacao esta cerca de {delayDays} dias atrasada.\n\nIsso pode acontecer por varios motivos, como variacao natural do ciclo, estresse, alteracoes de rotina ou outros fatores. Se houver chance de gravidez ou sintomas preocupantes, o ideal e fazer um teste ou procurar orientacao medica.";
    }

    private static string BuildNextPeriodReply(LumaUser user)
    {
        var preference = EnsurePreference(user);
        if (preference.LastPeriodStartDate is null)
        {
            return "Ainda nao tenho a data da sua ultima menstruacao para estimar a proxima. Voce pode me dizer algo como \"menstruei dia 10/04\".";
        }

        var expected = preference.LastPeriodStartDate.Value.AddDays(preference.AverageCycleLength);
        return $"Pela sua media atual, sua proxima menstruacao esta prevista para perto de {FormatDate(expected)}. Essa e uma estimativa, nao uma certeza.";
    }

    private static string BuildLastPeriodReply(LumaUser user)
    {
        var preference = EnsurePreference(user);
        return preference.LastPeriodStartDate is null
            ? "Ainda nao tenho uma ultima menstruacao registrada. Pode me mandar algo como \"menstruei dia 10/04\"."
            : $"Sua ultima menstruacao registrada foi em {FormatDate(preference.LastPeriodStartDate.Value)}.";
    }

    private async Task<string> BuildLastSymptomReplyAsync(Guid userId)
    {
        var ev = await db.CycleEvents
            .Where(ev => ev.UserId == userId && ev.Type == CycleEventTypes.Symptom)
            .OrderByDescending(ev => ev.Date)
            .ThenByDescending(ev => ev.CreatedAt)
            .FirstOrDefaultAsync();

        if (ev is null)
        {
            return "Ainda nao tenho sintomas registrados no seu historico.";
        }

        using var json = JsonDocument.Parse(ev.MetadataJson);
        var symptom = json.RootElement.TryGetProperty("symptom", out var value)
            ? SymptomLabel(value.GetString())
            : "um sintoma";

        return $"Seu ultimo sintoma registrado foi {symptom} em {FormatDate(ev.Date)}.";
    }

    private async Task<string> BuildLastSexualActivityReplyAsync(Guid userId)
    {
        var ev = await db.CycleEvents
            .Where(ev => ev.UserId == userId && ev.Type == CycleEventTypes.SexualActivity)
            .OrderByDescending(ev => ev.Date)
            .ThenByDescending(ev => ev.CreatedAt)
            .FirstOrDefaultAsync();

        return ev is null
            ? "Ainda nao tenho nenhuma relacao registrada no seu historico."
            : $"Sua ultima relacao registrada foi em {FormatDate(ev.Date)}. Esse dado aparece aqui apenas como historico pessoal, sem eu usar para afirmar gravidez ou diagnostico.";
    }

    private async Task<Pregnancy?> GetActivePregnancyAsync(Guid userId)
    {
        return await db.Pregnancies
            .Where(pregnancy => pregnancy.UserId == userId && pregnancy.Status == PregnancyStatus.Active)
            .OrderByDescending(pregnancy => pregnancy.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private async Task<string> HandlePregnancyPositiveAsync(LumaUser user, ConversationIntent intent, DateOnly today)
    {
        var pregnancy = await GetActivePregnancyAsync(user.Id);
        if (pregnancy is null)
        {
            pregnancy = new Pregnancy
            {
                UserId = user.Id,
                Status = PregnancyStatus.Active,
                StartReference = "positive_test"
            };
            db.Pregnancies.Add(pregnancy);
        }

        ApplyPregnancyReference(pregnancy, intent, today);
        await AddCycleEventAsync(user.Id, null, CycleEventTypes.PregnancyPositive, today, new
        {
            pregnancy_id = pregnancy.Id,
            gestational_weeks = pregnancy.GestationalWeeksAtRegistration,
            last_period_date = pregnancy.LastPeriodDate,
            estimated_due_date = pregnancy.EstimatedDueDate
        });

        if (pregnancy.LastPeriodDate is not null || pregnancy.GestationalWeeksAtRegistration is not null || pregnancy.EstimatedDueDate is not null)
        {
            user.PendingAction = null;
            return PregnancySummaryMessage(pregnancy);
        }

        user.PendingAction = PendingActions.AwaitingPregnancyReference;
        return "Obrigada por me contar. Posso te ajudar a organizar essas informacoes por aqui, sempre como apoio aos seus registros e sem substituir seu pre-natal.\n\nPara eu organizar seu acompanhamento, voce sabe alguma dessas informacoes?\n\n1. Data da ultima menstruacao\n2. Quantas semanas de gravidez\n3. Data provavel do parto\n4. Ainda nao sei";
    }

    private async Task<string> HandlePregnancyReferenceAsync(LumaUser user, string body, string rawBody, ConversationIntent intent, DateOnly today)
    {
        var pregnancy = await GetActivePregnancyAsync(user.Id);
        if (pregnancy is null)
        {
            pregnancy = new Pregnancy
            {
                UserId = user.Id,
                Status = PregnancyStatus.Active
            };
            db.Pregnancies.Add(pregnancy);
        }

        if (DateParser.IsUnknown(body) || body is "4")
        {
            pregnancy.StartReference = "unknown";
            pregnancy.UpdatedAt = DateTimeOffset.UtcNow;
            user.PendingAction = null;
            return "Tudo bem. Deixei registrado que voce ainda nao sabe a referencia da gravidez. Quando souber a data da ultima menstruacao, semanas ou data provavel do parto, pode me mandar por aqui.";
        }

        var reference = intent;
        reference.LastPeriodDate ??= IsLastPeriodReference(body) ? DateParser.ParseFlexibleDate(rawBody, today) : null;
        reference.GestationalWeeks ??= ParseGestationalWeeks(body);
        reference.EstimatedDueDate ??= IsDueDateReference(body) ? ParseUpcomingDate(rawBody, today) : null;

        if (reference.LastPeriodDate is null && reference.GestationalWeeks is null && reference.EstimatedDueDate is null)
        {
            return MisunderstoodMessage("Pode responder como \"minha ultima menstruacao foi dia 01/03\", \"estou de 8 semanas\", \"minha DPP e 06/12\" ou \"ainda nao sei\".");
        }

        ApplyPregnancyReference(pregnancy, reference, today);
        user.PendingAction = null;
        return PregnancySummaryMessage(pregnancy);
    }

    private static void ApplyPregnancyReference(Pregnancy pregnancy, ConversationIntent intent, DateOnly today)
    {
        if (intent.GestationalWeeks is not null)
        {
            pregnancy.GestationalWeeksAtRegistration = intent.GestationalWeeks;
            pregnancy.LastPeriodDate = today.AddDays(-(intent.GestationalWeeks.Value * 7));
            pregnancy.EstimatedDueDate = pregnancy.LastPeriodDate.Value.AddDays(280);
            pregnancy.StartReference = "gestational_weeks";
        }

        if (intent.LastPeriodDate is not null)
        {
            pregnancy.LastPeriodDate = intent.LastPeriodDate;
            pregnancy.EstimatedDueDate = intent.LastPeriodDate.Value.AddDays(280);
            pregnancy.GestationalWeeksAtRegistration = Math.Max(0, (today.DayNumber - intent.LastPeriodDate.Value.DayNumber) / 7);
            pregnancy.StartReference = "last_period";
        }

        if (intent.EstimatedDueDate is not null)
        {
            pregnancy.EstimatedDueDate = intent.EstimatedDueDate;
            pregnancy.StartReference = "estimated_due_date";
        }

        pregnancy.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string PregnancySummaryMessage(Pregnancy pregnancy)
    {
        var parts = new List<string>
        {
            "Pronto, deixei sua gravidez registrada para acompanhamento por aqui."
        };

        if (pregnancy.GestationalWeeksAtRegistration is not null)
        {
            parts.Add($"Pelos dados informados, a estimativa inicial e de cerca de {pregnancy.GestationalWeeksAtRegistration} semanas.");
        }

        if (pregnancy.EstimatedDueDate is not null)
        {
            parts.Add($"A data provavel do parto fica por volta de {FormatDate(pregnancy.EstimatedDueDate.Value)}.");
        }

        parts.Add("Vou tratar esses dados sempre como estimativas e apoio ao seu historico, sem substituir seu pre-natal.");
        return string.Join("\n\n", parts);
    }

    private async Task<string> BuildPregnancyWeeksReplyAsync(Guid userId, DateOnly today)
    {
        var pregnancy = await GetActivePregnancyAsync(userId);
        if (pregnancy?.LastPeriodDate is null)
        {
            return "Ainda nao tenho dados suficientes para estimar as semanas de gravidez. Voce pode me mandar a data da ultima menstruacao, quantas semanas tem hoje ou a data provavel do parto.";
        }

        var weeks = Math.Max(0, (today.DayNumber - pregnancy.LastPeriodDate.Value.DayNumber) / 7);
        return $"Pelos seus registros, voce esta com cerca de {weeks} semanas de gravidez. Essa e uma estimativa; confirme sempre no pre-natal.";
    }

    private async Task<string> BuildPregnancyDueDateReplyAsync(Guid userId)
    {
        var pregnancy = await GetActivePregnancyAsync(userId);
        if (pregnancy?.EstimatedDueDate is null)
        {
            return "Ainda nao tenho dados suficientes para estimar a data provavel do parto. Voce pode me mandar a data da ultima menstruacao ou quantas semanas tem hoje.";
        }

        return $"Pelos seus registros, a data provavel do parto esta por volta de {FormatDate(pregnancy.EstimatedDueDate.Value)}. Essa e uma estimativa e deve ser confirmada no seu pre-natal.";
    }

    private async Task<string> HandlePregnancyBleedingAsync(Guid userId, DateOnly date)
    {
        await AddCycleEventAsync(userId, null, CycleEventTypes.PregnancyBleeding, date, new { });
        return "Registrei o sangramento para seu historico.\n\nSangramentos na gravidez podem ter varias causas, algumas simples e outras que precisam de avaliacao. Como voce esta gravida, e mais seguro entrar em contato com seu medico ou obstetra, principalmente se o sangramento for intenso, vier com dor forte, tontura, febre ou mal-estar.";
    }

    private static int CountCapturedFields(OnboardingExtraction? captured)
    {
        if (captured is null)
        {
            return 0;
        }

        var count = 0;
        if (captured.DisplayName is not null) count++;
        if (captured.IsAdultConfirmed is not null) count++;
        if (captured.LastPeriodStartDate is not null || captured.LastPeriodDaysAgo is not null || captured.LastPeriodUnknown) count++;
        if (captured.AverageCycleLength is not null) count++;
        if (captured.AveragePeriodLength is not null) count++;
        if (captured.ContraceptiveType is not null) count++;
        return count;
    }

    private static bool HasExplicitAgeEvidence(string body)
    {
        return IsAffirmative(body)
            || IsNegative(body)
            || body.Contains("anos", StringComparison.Ordinal)
            || body.Contains("idade", StringComparison.Ordinal)
            || body.Contains("maior de idade", StringComparison.Ordinal)
            || body.Contains("maior", StringComparison.Ordinal)
            || body.Contains("18+", StringComparison.Ordinal);
    }

    private static bool ShouldUseAiForOnboarding(string rawBody)
    {
        return rawBody.Any(char.IsLetter) && rawBody.Trim().Length >= 8;
    }

    private static string? TryExtractDisplayName(string body, string rawBody)
    {
        var markers = new[]
        {
            "meu nome e ",
            "me chamo ",
            "pode me chamar de ",
            "sou a ",
            "sou "
        };

        foreach (var marker in markers)
        {
            var index = body.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var normalizedCandidate = body[(index + marker.Length)..].Split(',', '.', ';', '\n').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                continue;
            }

            var rawCandidate = rawBody.Trim().Split(',', '.', ';', '\n')
                .FirstOrDefault(part => MessageText.Normalize(part).Contains(normalizedCandidate.Trim(), StringComparison.Ordinal));

            var candidate = rawCandidate is null
                ? normalizedCandidate.Trim()
                : rawCandidate[Math.Max(0, rawCandidate.Length - normalizedCandidate.Length)..].Trim();

            var firstWord = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstWord is { Length: >= 2 and <= 60 } && firstWord.All(ch => char.IsLetter(ch) || ch is '\'' or '-'))
            {
                return char.ToUpperInvariant(firstWord[0]) + firstWord[1..];
            }
        }

        return null;
    }

    private static bool IsLikelyPlainDisplayName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 2 or > 60)
        {
            return false;
        }

        var normalized = MessageText.Normalize(trimmed);
        var blockedTerms = new[]
        {
            "ciclo",
            "menstru",
            "idade",
            "anos",
            "dia ",
            "hoje",
            "ontem",
            "aceito",
            "nao",
            "sim"
        };

        if (blockedTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal)))
        {
            return false;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 2 && words.All(word => word.All(ch => char.IsLetter(ch) || ch is '\'' or '-'));
    }

    private static int? TryExtractAge(string body)
    {
        if (!body.Contains("anos", StringComparison.Ordinal) && !body.Contains("idade", StringComparison.Ordinal))
        {
            return null;
        }

        var age = MessageText.ExtractFirstInteger(body);
        return age is >= 1 and <= 120 ? age : null;
    }

    private static bool IsAffirmative(string body)
    {
        return body is "1" or "sim" or "s" or "aceito" or "aceitar" or "claro" or "ok" or "okay"
            || body.Contains("aceito", StringComparison.Ordinal)
            || body.Contains("concordo", StringComparison.Ordinal)
            || body.Contains("claro", StringComparison.Ordinal)
            || body.Contains("com certeza", StringComparison.Ordinal)
            || body.Contains("pode", StringComparison.Ordinal) && body.Contains("sim", StringComparison.Ordinal);
    }

    private static bool IsNegative(string body)
    {
        return body is "2" or "nao" or "n"
            || body.Contains("nao aceito", StringComparison.Ordinal);
    }

    private static bool IsHelp(string body)
    {
        return body is "ajuda" or "help" or "menu";
    }

    private static bool IsGreeting(string body)
    {
        var firstToken = body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim(',', '.', '!', '?', ';', ':');

        return firstToken is "oi" or "ola"
            || body is "bom dia" or "boa tarde" or "boa noite"
            || body.StartsWith("oi ", StringComparison.Ordinal)
            || body.StartsWith("ola ", StringComparison.Ordinal);
    }

    private static bool IsPeriodStart(string body)
    {
        return body.Contains("menstruei", StringComparison.Ordinal)
            || body.Contains("menstruacao comecou", StringComparison.Ordinal)
            || body.Contains("comecou minha menstruacao", StringComparison.Ordinal)
            || body.Contains("comecei a menstruar", StringComparison.Ordinal)
            || body.Contains("desceu", StringComparison.Ordinal)
            || body.Contains("veio", StringComparison.Ordinal) && body.Contains("menstru", StringComparison.Ordinal);
    }

    private static bool IsPeriodEnd(string body)
    {
        return body.Contains("acabou", StringComparison.Ordinal)
            || body.Contains("parou", StringComparison.Ordinal)
            || body.Contains("terminou", StringComparison.Ordinal);
    }

    private static bool IsDelayQuestion(string body)
    {
        return body.Contains("atras", StringComparison.Ordinal) || body.Contains("atrasada", StringComparison.Ordinal);
    }

    private static bool IsNextPeriodQuestion(string body)
    {
        return body.Contains("proxima", StringComparison.Ordinal)
            || body.Contains("quando", StringComparison.Ordinal) && body.Contains("menstru", StringComparison.Ordinal);
    }

    private static bool IsLastPeriodQuestion(string body)
    {
        return body.Contains("ultima menstruacao", StringComparison.Ordinal)
            || body.Contains("ultima vez que menstruei", StringComparison.Ordinal)
            || body.Contains("quando foi minha ultima", StringComparison.Ordinal) && body.Contains("menstru", StringComparison.Ordinal);
    }

    private static bool IsLastSymptomQuestion(string body)
    {
        return body.Contains("ultimo sintoma", StringComparison.Ordinal)
            || body.Contains("sintoma registrado", StringComparison.Ordinal);
    }

    private static bool IsLastSexualActivityQuestion(string body)
    {
        return (body.Contains("ultima", StringComparison.Ordinal) || body.Contains("quando", StringComparison.Ordinal))
            && (body.Contains("relacao sexual", StringComparison.Ordinal)
                || body.Contains("relacao", StringComparison.Ordinal)
                || body.Contains("sexo", StringComparison.Ordinal)
                || body.Contains("transa", StringComparison.Ordinal));
    }

    private static bool IsSexualActivity(string body)
    {
        return body.Contains("relacao", StringComparison.Ordinal)
            || body.Contains("sexo", StringComparison.Ordinal)
            || body.Contains("transa", StringComparison.Ordinal);
    }

    private static bool IsPregnancyPositiveStatement(string body)
    {
        if (LooksLikeQuestion(body)
            && !body.Contains("teste deu positivo", StringComparison.Ordinal)
            && !body.Contains("descobri", StringComparison.Ordinal))
        {
            return false;
        }

        return body.Contains("descobri que estou gravida", StringComparison.Ordinal)
            || body.Contains("estou gravida", StringComparison.Ordinal)
            || body.Contains("to gravida", StringComparison.Ordinal)
            || body.Contains("tou gravida", StringComparison.Ordinal)
            || body.Contains("gravida de", StringComparison.Ordinal)
            || body.Contains("teste deu positivo", StringComparison.Ordinal)
            || body.Contains("meu teste deu positivo", StringComparison.Ordinal);
    }

    private static bool IsPregnancyBleeding(string body)
    {
        return body.Contains("sangramento", StringComparison.Ordinal)
            || body.Contains("sangrei", StringComparison.Ordinal)
            || body.Contains("sangrando", StringComparison.Ordinal);
    }

    private static bool IsPregnancySymptom(string body)
    {
        return body.Contains("nausea", StringComparison.Ordinal)
            || body.Contains("enjoo", StringComparison.Ordinal)
            || body.Contains("azia", StringComparison.Ordinal)
            || body.Contains("tontura", StringComparison.Ordinal)
            || body.Contains("sono", StringComparison.Ordinal)
            || body.Contains("cansaco", StringComparison.Ordinal)
            || body.Contains("colica", StringComparison.Ordinal)
            || body.Contains("dor", StringComparison.Ordinal);
    }

    private static bool IsPrenatalAppointment(string body)
    {
        return body.Contains("pre natal", StringComparison.Ordinal)
            || body.Contains("prenatal", StringComparison.Ordinal)
            || body.Contains("obstetra", StringComparison.Ordinal)
            || body.Contains("consulta", StringComparison.Ordinal) && body.Contains("gest", StringComparison.Ordinal);
    }

    private static bool IsUltrasound(string body)
    {
        return body.Contains("ultrassom", StringComparison.Ordinal)
            || body.Contains("ultra", StringComparison.Ordinal);
    }

    private static bool IsPregnancyWeeksQuestion(string body)
    {
        return (body.Contains("quantas semanas", StringComparison.Ordinal)
                || body.Contains("de quantas semanas", StringComparison.Ordinal)
                || body.Contains("semanas estou", StringComparison.Ordinal))
            && !IsPregnancyPositiveStatement(body);
    }

    private static bool IsPregnancyDueDateQuestion(string body)
    {
        return body.Contains("data provavel do parto", StringComparison.Ordinal)
            || body.Contains("dpp", StringComparison.Ordinal)
            || body.Contains("quando nasce", StringComparison.Ordinal)
            || body.Contains("quando e o parto", StringComparison.Ordinal);
    }

    private static bool IsLumaIdentityQuestion(string body)
    {
        return body.Contains("quem e voce", StringComparison.Ordinal)
            || body.Contains("quem voce e", StringComparison.Ordinal)
            || body.Contains("o que voce faz", StringComparison.Ordinal)
            || body.Contains("o que e a luma", StringComparison.Ordinal)
            || body.Contains("quem e a luma", StringComparison.Ordinal);
    }

    private static bool LooksLikeQuestion(string body)
    {
        return body.Contains("?", StringComparison.Ordinal)
            || body.StartsWith("qual ", StringComparison.Ordinal)
            || body.StartsWith("quando ", StringComparison.Ordinal)
            || body.StartsWith("quem ", StringComparison.Ordinal)
            || body.StartsWith("como ", StringComparison.Ordinal)
            || body.StartsWith("posso ", StringComparison.Ordinal)
            || body.StartsWith("devo ", StringComparison.Ordinal)
            || body.StartsWith("sera ", StringComparison.Ordinal)
            || body.StartsWith("luma,", StringComparison.Ordinal);
    }

    private static int? ParseGestationalWeeks(string body)
    {
        if (!body.Contains("semana", StringComparison.Ordinal))
        {
            return null;
        }

        var weeks = MessageText.ExtractFirstInteger(body);
        return weeks is >= 1 and <= 45 ? weeks : null;
    }

    private static bool IsLastPeriodReference(string body)
    {
        return body.Contains("ultima menstruacao", StringComparison.Ordinal)
            || body.Contains("menstruei", StringComparison.Ordinal)
            || body.Contains("dum", StringComparison.Ordinal);
    }

    private static bool IsDueDateReference(string body)
    {
        return body.Contains("data provavel", StringComparison.Ordinal)
            || body.Contains("dpp", StringComparison.Ordinal)
            || body.Contains("parto", StringComparison.Ordinal);
    }

    private static bool LooksLikeKnownCompletedIntent(string body, ConversationIntent? intent = null)
    {
        if (!string.IsNullOrWhiteSpace(intent?.Intent) && intent.Intent != ConversationIntents.OutOfScope)
        {
            return true;
        }

        return IsHelp(body)
            || IsGreeting(body)
            || IsPeriodStart(body)
            || IsPeriodEnd(body)
            || IsDelayQuestion(body)
            || IsNextPeriodQuestion(body)
            || IsLastPeriodQuestion(body)
            || IsLastSymptomQuestion(body)
            || IsLastSexualActivityQuestion(body)
            || IsSexualActivity(body)
            || IsPregnancyPositiveStatement(body)
            || IsPregnancyBleeding(body)
            || IsPrenatalAppointment(body)
            || IsUltrasound(body)
            || IsPregnancyWeeksQuestion(body)
            || IsPregnancyDueDateQuestion(body)
            || IsLumaIdentityQuestion(body)
            || ParseFlowIntensity(body) is not null
            || ParseSymptoms(body).Count > 0
            || ParseMood(body) is not null;
    }

    private static bool IsFlowOnlyResponse(string body)
    {
        return ParseFlowIntensity(body) is not null
            && !IsPeriodStart(body)
            && !IsPeriodEnd(body)
            && ParseSymptoms(body).Count == 0
            && ParseMood(body) is null
            && !IsSexualActivity(body)
            && !IsDelayQuestion(body)
            && !IsNextPeriodQuestion(body)
            && !IsLastPeriodQuestion(body)
            && !IsLastSymptomQuestion(body)
            && !IsLastSexualActivityQuestion(body)
            && !IsPregnancyPositiveStatement(body)
            && !IsPregnancyBleeding(body)
            && !IsPrenatalAppointment(body)
            && !IsUltrasound(body)
            && !IsPregnancyWeeksQuestion(body)
            && !IsPregnancyDueDateQuestion(body);
    }

    private static bool IsActionablePendingIntent(string? intent)
    {
        return intent is ConversationIntents.PeriodStart
            or ConversationIntents.SexualActivity
            or ConversationIntents.PregnancyPositive;
    }

    private static bool IsOutOfOrderForCurrentStep(string onboardingStep, string? intent)
    {
        if (!IsActionablePendingIntent(intent))
        {
            return false;
        }

        if (onboardingStep == OnboardingSteps.AwaitingLastPeriodStart && intent == ConversationIntents.PeriodStart)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldAskAiForOutOfOrderIntent(string body, string rawBody)
    {
        if (!ShouldUseAiForOnboarding(rawBody) || IsGreeting(body) || IsLikelyPlainDisplayName(rawBody.Trim()))
        {
            return false;
        }

        return !body.Contains("ciclo", StringComparison.Ordinal)
            && !body.Contains("dura", StringComparison.Ordinal)
            && !body.Contains("anos", StringComparison.Ordinal)
            && !body.Contains("idade", StringComparison.Ordinal);
    }

    private static bool IsFixedGuardrailReply(string reply)
    {
        var normalized = MessageText.Normalize(reply);
        return normalized.Contains("nao consigo confirmar", StringComparison.Ordinal)
            || normalized.Contains("nao posso afirmar", StringComparison.Ordinal)
            || normalized.Contains("procure atendimento", StringComparison.Ordinal)
            || normalized.Contains("menores de 18 anos", StringComparison.Ordinal)
            || normalized.Contains("sem o seu consentimento", StringComparison.Ordinal);
    }

    private static bool IsRequiredBackendPrompt(LumaUser user, string reply)
    {
        if (user.OnboardingStep != OnboardingSteps.Completed)
        {
            return true;
        }

        var normalized = MessageText.Normalize(reply);
        return normalized.Contains("voce aceita?", StringComparison.Ordinal)
            || normalized.Contains("como devo te chamar", StringComparison.Ordinal)
            || normalized.Contains("18 anos ou mais", StringComparison.Ordinal)
            || normalized.Contains("primeiro dia da sua ultima menstruacao", StringComparison.Ordinal)
            || normalized.Contains("ciclo costuma ter quantos dias", StringComparison.Ordinal)
            || normalized.Contains("menstruacao costuma durar", StringComparison.Ordinal)
            || normalized.Contains("metodo contraceptivo", StringComparison.Ordinal)
            || normalized.Contains("como esta o fluxo?", StringComparison.Ordinal)
            || normalized.Contains("1. leve", StringComparison.Ordinal);
    }

    private static string? ExtractPendingString(string payloadJson, string property)
    {
        try
        {
            using var json = JsonDocument.Parse(payloadJson);
            return json.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatRelativeDateForReply(DateOnly date, DateOnly today)
    {
        if (date == today)
        {
            return "hoje";
        }

        if (date == today.AddDays(-1))
        {
            return "ontem";
        }

        return $"em {FormatDate(date)}";
    }

    private static DateOnly InferPeriodStartDate(string body, string rawBody, DateOnly today)
    {
        if (body.Contains("ontem", StringComparison.Ordinal)
            && (body.Contains("desceu", StringComparison.Ordinal)
                || body.Contains("menstruei", StringComparison.Ordinal)
                || body.Contains("veio", StringComparison.Ordinal)
                || body.Contains("comecou", StringComparison.Ordinal)))
        {
            return today.AddDays(-1);
        }

        return DateParser.ParseFlexibleDate(rawBody, today) ?? today;
    }

    private static DateOnly? ParseUpcomingDate(string rawBody, DateOnly today)
    {
        var parsed = DateParser.ParseFlexibleDate(rawBody, today);
        if (parsed is null)
        {
            return null;
        }

        while (parsed.Value < today.AddMonths(-6))
        {
            parsed = parsed.Value.AddYears(1);
        }

        return parsed;
    }

    private static string? ParseContraceptiveType(string body)
    {
        if (body.Contains("prefiro nao", StringComparison.Ordinal) || body.Contains("nao informar", StringComparison.Ordinal))
        {
            return "prefer_not_say";
        }

        if (body is "1" || body.Contains("nao uso", StringComparison.Ordinal) || body.Contains("nenhum", StringComparison.Ordinal))
        {
            return "none";
        }

        if (body is "2" || body.Contains("pilula", StringComparison.Ordinal) || body.Contains("anticoncepcional", StringComparison.Ordinal))
        {
            return "pill";
        }

        if (body is "3" || body.Contains("injec", StringComparison.Ordinal))
        {
            return "injection";
        }

        if (body is "4" || body.Contains("diu hormonal", StringComparison.Ordinal) || body.Contains("mirena", StringComparison.Ordinal))
        {
            return "hormonal_iud";
        }

        if (body is "5" || body.Contains("diu de cobre", StringComparison.Ordinal) || body.Contains("diu cobre", StringComparison.Ordinal))
        {
            return "copper_iud";
        }

        if (body.Contains("diu", StringComparison.Ordinal))
        {
            return "hormonal_iud";
        }

        if (body is "6" || body.Contains("implante", StringComparison.Ordinal))
        {
            return "implant";
        }

        if (body is "7" || body.Contains("camisinha", StringComparison.Ordinal) || body.Contains("preservativo", StringComparison.Ordinal))
        {
            return "condom";
        }

        if (body is "8" || body.Contains("outro", StringComparison.Ordinal))
        {
            return "other";
        }

        if (body is "9")
        {
            return "prefer_not_say";
        }

        return null;
    }

    private static string? ParseFlowIntensity(string body)
    {
        if (body is "4" || body.Contains("prefiro nao", StringComparison.Ordinal))
        {
            return "unknown";
        }

        if (body is "3"
            || body.Contains("intenso", StringComparison.Ordinal)
            || body.Contains("intensa", StringComparison.Ordinal)
            || body.Contains("forte", StringComparison.Ordinal)
            || body.Contains("muito", StringComparison.Ordinal))
        {
            return "intense";
        }

        if (body is "2" || body.Contains("medio", StringComparison.Ordinal) || body.Contains("moderado", StringComparison.Ordinal))
        {
            return "medium";
        }

        if (body is "1" || body.Contains("leve", StringComparison.Ordinal) || body.Contains("pouco", StringComparison.Ordinal))
        {
            return "light";
        }

        return null;
    }

    private static List<(string Key, string Label, string Intensity)> ParseSymptoms(string body)
    {
        var intensity = ParseSymptomIntensity(body);
        var symptoms = new List<(string Key, string Label, string Intensity)>();

        AddSymptomIf(body, symptoms, "colica", "cramp", "colica", intensity);
        AddSymptomIf(body, symptoms, "dor de cabeca", "headache", "dor de cabeca", intensity);
        AddSymptomIf(body, symptoms, "nausea", "nausea", "nausea", intensity);
        AddSymptomIf(body, symptoms, "enjoo", "nausea", "nausea", intensity);
        AddSymptomIf(body, symptoms, "sensibilidade nos seios", "breast_tenderness", "sensibilidade nos seios", intensity);
        AddSymptomIf(body, symptoms, "seios doloridos", "breast_tenderness", "sensibilidade nos seios", intensity);
        AddSymptomIf(body, symptoms, "inchaco", "bloating", "inchaco", intensity);
        AddSymptomIf(body, symptoms, "acne", "acne", "acne", intensity);
        AddSymptomIf(body, symptoms, "dor lombar", "back_pain", "dor lombar", intensity);
        AddSymptomIf(body, symptoms, "sangramento fora", "spotting", "sangramento fora do periodo", intensity);
        AddSymptomIf(body, symptoms, "corrimento", "discharge", "corrimento", intensity);
        AddSymptomIf(body, symptoms, "cansaco", "tiredness", "cansaco", intensity);
        AddSymptomIf(body, symptoms, "insonia", "insomnia", "insonia", intensity);
        AddSymptomIf(body, symptoms, "desejo alimentar", "food_craving", "desejo alimentar", intensity);

        return symptoms
            .GroupBy(symptom => symptom.Key)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddSymptomIf(string body, List<(string Key, string Label, string Intensity)> symptoms, string needle, string key, string label, string intensity)
    {
        if (body.Contains(needle, StringComparison.Ordinal))
        {
            symptoms.Add((key, $"{label} {IntensityLabel(intensity)}", intensity));
        }
    }

    private static string ParseSymptomIntensity(string body)
    {
        if (body.Contains("absurda", StringComparison.Ordinal)
            || body.Contains("absurdo", StringComparison.Ordinal)
            || body.Contains("forte", StringComparison.Ordinal)
            || body.Contains("intensa", StringComparison.Ordinal)
            || body.Contains("muito", StringComparison.Ordinal))
        {
            return "strong";
        }

        if (body.Contains("leve", StringComparison.Ordinal))
        {
            return "light";
        }

        return "moderate";
    }

    private static (string Key, string Label)? ParseMood(string body)
    {
        if (body.Contains("irritada", StringComparison.Ordinal) || body.Contains("irritado", StringComparison.Ordinal))
        {
            return ("irritable", "irritada");
        }

        if (body.Contains("triste", StringComparison.Ordinal))
        {
            return ("sad", "triste");
        }

        if (body.Contains("ansiosa", StringComparison.Ordinal) || body.Contains("ansioso", StringComparison.Ordinal))
        {
            return ("anxious", "ansiosa");
        }

        if (body.Contains("sensivel", StringComparison.Ordinal))
        {
            return ("sensitive", "sensivel");
        }

        if (body.Contains("cansada", StringComparison.Ordinal) || body.Contains("cansado", StringComparison.Ordinal))
        {
            return ("tired", "cansada");
        }

        if (body.Contains("com energia", StringComparison.Ordinal) || body.Contains("disposta", StringComparison.Ordinal))
        {
            return ("energetic", "com energia");
        }

        if (body.Contains("bem", StringComparison.Ordinal) || body.Contains("feliz", StringComparison.Ordinal))
        {
            return ("well", "bem");
        }

        return null;
    }

    private static string ParseProtectedValue(string body)
    {
        if (body.Contains("sem camisinha", StringComparison.Ordinal) || body.Contains("sem protecao", StringComparison.Ordinal) || body.Contains("desproteg", StringComparison.Ordinal))
        {
            return "no";
        }

        if (body.Contains("camisinha", StringComparison.Ordinal) || body.Contains("preservativo", StringComparison.Ordinal) || body.Contains("com protecao", StringComparison.Ordinal))
        {
            return "yes";
        }

        if (body.Contains("prefiro nao", StringComparison.Ordinal))
        {
            return "prefer_not_say";
        }

        return "unknown";
    }

    private static object BuildSexualActivityMetadata(string body, ConversationIntent intent)
    {
        var protectedValue = intent.Protected ?? ParseProtectedValue(body);
        var method = body.Contains("camisinha", StringComparison.Ordinal) || body.Contains("preservativo", StringComparison.Ordinal)
            ? "condom"
            : body.Contains("pilula", StringComparison.Ordinal) || body.Contains("anticoncepcional", StringComparison.Ordinal)
                ? "pill"
                : "unknown";

        return new
        {
            protected_value = protectedValue,
            @protected = protectedValue,
            contraceptive_method = method
        };
    }

    private static string SymptomLabel(string? key)
    {
        return key switch
        {
            "cramp" => "colica",
            "headache" => "dor de cabeca",
            "nausea" => "nausea",
            "breast_tenderness" => "sensibilidade nos seios",
            "bloating" => "inchaco",
            "acne" => "acne",
            "back_pain" => "dor lombar",
            "spotting" => "sangramento fora do periodo",
            "discharge" => "corrimento",
            "tiredness" => "cansaco",
            "insomnia" => "insonia",
            "food_craving" => "desejo alimentar",
            _ => "um sintoma"
        };
    }

    private static string IntensityLabel(string intensity)
    {
        return intensity switch
        {
            "strong" => "forte",
            "light" => "leve",
            _ => "moderada"
        };
    }

    private static string FlowLabel(string flow)
    {
        return flow switch
        {
            "light" => "leve",
            "medium" => "medio",
            "intense" => "intenso",
            _ => "nao informado"
        };
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("dd/MM");
    }
}
