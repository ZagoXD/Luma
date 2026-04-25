using System.Text.Json;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public sealed class ConversationService(
    LumaDbContext db,
    IConfiguration configuration,
    IOnboardingDataExtractor onboardingAi,
    IDateProvider dateProvider,
    ILogger<ConversationService> logger)
{
    private readonly bool _storeMessageBodies = configuration.GetValue("Luma:StoreMessageBodies", false);

    public async Task<string> HandleIncomingMessageAsync(IncomingMessage incoming)
    {
        var phone = PhoneNumber.Normalize(incoming.From);
        var user = await db.Users.Include(existing => existing.Preference)
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

        var reply = await BuildReplyAsync(user, incoming.Body);

        db.Messages.Add(new ConversationMessage
        {
            UserId = user.Id,
            Direction = "outbound",
            Provider = incoming.Provider,
            Body = _storeMessageBodies ? reply : null
        });

        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Processed message for user {UserId} at step {Step}",
            user.Id,
            user.OnboardingStep);

        return reply;
    }

    private async Task<string> BuildReplyAsync(LumaUser user, string rawBody)
    {
        var body = MessageText.Normalize(rawBody);

        if (user.OnboardingStep is OnboardingSteps.ConsentDeclined)
        {
            if (IsAffirmative(body))
            {
                return await AcceptConsentAsync(user);
            }

            return InitialConsentMessage();
        }

        if (user.OnboardingStep is OnboardingSteps.UnderageBlocked)
        {
            return "Por segurança, a Luma ainda não pode continuar esse cadastro pelo WhatsApp para menores de 18 anos.";
        }

        if (user.OnboardingStep != OnboardingSteps.Completed)
        {
            return await ContinueOnboardingAsync(user, body, rawBody);
        }

        if (SafetyGuardrail.ShouldBlock(body))
        {
            return SafetyGuardrail.SafeReply;
        }

        if (user.PendingAction == PendingActions.AwaitingFlowIntensity)
        {
            var pendingFlow = ParseFlowIntensity(body);
            if (pendingFlow is null)
            {
                return "Como está o fluxo?\n1. Leve\n2. Médio\n3. Intenso\n4. Prefiro não informar";
            }

            user.PendingAction = null;
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.FlowUpdate, Today(), new
            {
                flow_intensity = pendingFlow
            });

            return pendingFlow == "unknown"
                ? "Tudo bem, deixei o fluxo de hoje sem informar."
                : $"Atualizado. Hoje ficou registrado como fluxo {FlowLabel(pendingFlow)}.";
        }

        return await HandleCompletedUserMessageAsync(user, body, rawBody);
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
                    return "Tudo bem. Sem o seu consentimento eu não posso armazenar dados do ciclo ou continuar o cadastro. Se mudar de ideia, responda \"aceito\".";
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
                    return MisunderstoodMessage("Pode responder só com seu primeiro nome ou apelido. Por exemplo: \"Nay\" ou \"Pode me chamar de Nay\".");
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
                    return "Obrigada por responder. Por segurança, a Luma ainda não pode continuar esse cadastro pelo WhatsApp para menores de 18 anos.";
                }

                return MisunderstoodMessage("Você pode responder \"sim\", \"não\" ou algo como \"tenho 23 anos\".");

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

                var date = DateParser.ParseFlexibleDate(rawBody, Today());
                if (date is null)
                {
                    return MisunderstoodMessage("Pode responder como \"10/04\", \"dia 10\", \"começou há 3 dias\", \"ontem\" ou \"não lembro\".");
                }

                EnsurePreference(user).LastPeriodStartDate = date.Value;
                var onboardingCycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, date.Value, CycleStatus.Unknown);
                await AddCycleEventAsync(user.Id, onboardingCycle.Id, CycleEventTypes.PeriodStart, date.Value, new
                {
                    onboarding = true
                });
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
                    return MisunderstoodMessage("Me diga um número entre 21 e 45 dias. Se não souber, responda \"não sei\" e uso 28 dias por enquanto.");
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
                    return MisunderstoodMessage("Me diga um número entre 2 e 10 dias para a duração média da menstruação.");
                }

                EnsurePreference(user).AveragePeriodLength = periodLength.Value;
                user.OnboardingStep = OnboardingSteps.Completed;
                return NextOnboardingPrompt(user, new OnboardingExtraction { AveragePeriodLength = periodLength.Value });

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
                    await AddCycleEventAsync(user.Id, onboardingCycle.Id, CycleEventTypes.PeriodStart, extraction.LastPeriodStartDate.Value, new
                    {
                        onboarding = true
                    });
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

        if ((body.Contains("dura", StringComparison.Ordinal) || body.Contains("menstruacao", StringComparison.Ordinal) || body.Contains("menstruação", StringComparison.Ordinal))
            && firstInteger is >= 2 and <= 10)
        {
            extraction.AveragePeriodLength = firstInteger;
        }

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
            "meu nome é ",
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

            var normalizedNameStart = index + marker.Length;
            var normalizedCandidate = body[normalizedNameStart..].Split(',', '.', ';', '\n').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                continue;
            }

            var rawCandidate = rawBody.Trim().Split(',', '.', ';', '\n')
                .FirstOrDefault(part => MessageText.Normalize(part).Contains(normalizedCandidate.Trim(), StringComparison.Ordinal));

            var candidate = rawCandidate is null
                ? normalizedCandidate.Trim()
                : rawCandidate[(Math.Max(0, rawCandidate.Length - normalizedCandidate.Length))..].Trim();

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
            "não",
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

    private static string NextOnboardingPrompt(LumaUser user, OnboardingExtraction? captured = null)
    {
        var firstContact = captured?.DisplayName is not null;
        var manyDetails = CountCapturedFields(captured) >= 2;
        var prefix = string.IsNullOrWhiteSpace(user.DisplayName) ? "" : $"{user.DisplayName}, ";

        return user.OnboardingStep switch
        {
            OnboardingSteps.AwaitingDisplayName => "Para eu deixar nossa conversa mais pessoal, como devo te chamar? Pode mandar só seu primeiro nome ou apelido.",
            OnboardingSteps.AwaitingAgeConfirmation => firstContact
                ? $"Olá, {user.DisplayName}, prazer em conhecê-la. Meu nome é Luma e vou ser sua assistente de ciclo por aqui.\n\nAntes de seguirmos, você poderia me confirmar se tem 18 anos ou mais?\n1. Sim\n2. Não"
                : $"{prefix}antes de continuar, você poderia me confirmar se tem 18 anos ou mais?\n1. Sim\n2. Não",
            OnboardingSteps.AwaitingLastPeriodStart => manyDetails
                ? $"{prefix}obrigada por já me passar essas informações. Agora me diz: qual foi o primeiro dia da sua última menstruação?\n\nPode responder tipo \"começou dia 10/04\" ou \"não lembro\"."
                : $"{prefix}obrigada por confirmar. Qual foi o primeiro dia da sua última menstruação?\n\nPode responder tipo \"começou dia 10/04\" ou \"não lembro\".",
            OnboardingSteps.AwaitingAverageCycleLength => manyDetails
                ? $"Prazer em conhecê-la, {user.DisplayName}. Obrigada por já me passar essas informações. Para finalizar seu cadastro, me diz: seu ciclo costuma ter quantos dias?\n\nSe não souber, posso começar usando 28 dias e ir ajustando com o tempo."
                : $"{prefix}perfeito, obrigada. Seu ciclo costuma ter quantos dias?\n\nSe não souber, posso começar usando 28 dias e ir ajustando com o tempo.",
            OnboardingSteps.AwaitingAveragePeriodLength => manyDetails
                ? $"{prefix}ótimo, já deixei esses dados iniciais organizados. Só falta uma coisinha: sua menstruação costuma durar quantos dias?"
                : $"{prefix}entendi. Sua menstruação costuma durar quantos dias?",
            OnboardingSteps.Completed => CompletedOnboardingMessage(user, captured),
            OnboardingSteps.UnderageBlocked => "Obrigada por responder. Por segurança, a Luma ainda não pode continuar esse cadastro pelo WhatsApp para menores de 18 anos.",
            _ => InitialConsentMessage()
        };
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
        return count;
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

    private async Task<string> HandleCompletedUserMessageAsync(LumaUser user, string body, string rawBody)
    {
        var today = Today();

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
            return "Eu posso apagar seus dados, mas essa ação precisa de confirmação. Para este MVP local, me avise no painel/admin ou implemente o fluxo definitivo antes de usar em produção.";
        }

        if (IsPeriodStart(body))
        {
            var date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            var periodFlow = ParseFlowIntensity(body);
            var cycle = await CreateOrUpdateCycleFromLastPeriodAsync(user.Id, date);
            EnsurePreference(user).LastPeriodStartDate = date;

            await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodStart, date, new { });

            if (periodFlow is not null)
            {
                await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.FlowUpdate, date, new { flow_intensity = periodFlow });
                return $"Registrei o início da sua menstruação em {FormatDate(date)} com fluxo {FlowLabel(periodFlow)}.";
            }

            user.PendingAction = PendingActions.AwaitingFlowIntensity;
            return $"Registrei o início da sua menstruação em {FormatDate(date)}.\n\nComo está o fluxo?\n1. Leve\n2. Médio\n3. Intenso\n4. Prefiro não informar";
        }

        if (IsPeriodEnd(body))
        {
            var date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            var cycle = await db.Cycles
                .Where(existing => existing.UserId == user.Id && existing.Status == CycleStatus.Ongoing)
                .OrderByDescending(existing => existing.StartDate)
                .FirstOrDefaultAsync();

            if (cycle is null)
            {
                await AddCycleEventAsync(user.Id, null, CycleEventTypes.PeriodEnd, date, new { });
                return $"Registrei que sua menstruação terminou em {FormatDate(date)}. Ainda não encontrei um ciclo aberto para calcular a duração.";
            }

            cycle.EndDate = date;
            cycle.Status = CycleStatus.Finished;
            cycle.UpdatedAt = DateTimeOffset.UtcNow;
            await AddCycleEventAsync(user.Id, cycle.Id, CycleEventTypes.PeriodEnd, date, new { });

            var days = Math.Max(1, date.DayNumber - cycle.StartDate.DayNumber + 1);
            var nextPeriod = cycle.StartDate.AddDays(EnsurePreference(user).AverageCycleLength);
            return $"Registrei que sua menstruação terminou em {FormatDate(date)}. Ela durou cerca de {days} dias neste ciclo. Pela sua média atual, a próxima menstruação está prevista para perto de {FormatDate(nextPeriod)}.";
        }

        var symptom = ParseSymptom(body);
        if (symptom is not null)
        {
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.Symptom, today, new
            {
                symptom = symptom.Value.Key,
                intensity = symptom.Value.Intensity
            });

            return $"Registrei {symptom.Value.Label} para hoje. Se vier com dor muito forte, sangramento intenso, febre, tontura ou mal-estar importante, procure orientação médica.";
        }

        if (IsDelayQuestion(body))
        {
            return BuildDelayReply(user, today);
        }

        if (IsNextPeriodQuestion(body))
        {
            return BuildNextPeriodReply(user);
        }

        if (IsSexualActivity(body))
        {
            var date = DateParser.ParseFlexibleDate(rawBody, today) ?? today;
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.SexualActivity, date, new { });
            return $"Registrei a relação em {FormatDate(date)}. Esse dado fica salvo apenas para seu histórico; eu não uso isso para afirmar gravidez ou diagnóstico.";
        }

        var flow = ParseFlowIntensity(body);
        if (flow is not null)
        {
            await AddCycleEventAsync(user.Id, await GetCurrentCycleIdAsync(user.Id), CycleEventTypes.FlowUpdate, today, new { flow_intensity = flow });
            return flow == "unknown"
                ? "Tudo bem, deixei o fluxo de hoje sem informar."
                : $"Registrei fluxo {FlowLabel(flow)} para hoje.";
        }

        return MisunderstoodMessage("Você pode tentar de um jeito mais direto, como \"menstruei hoje\", \"acabou ontem\", \"fluxo intenso\", \"tô com cólica forte\" ou \"quando é minha próxima menstruação?\".");
    }

    private async Task<Cycle> CreateOrUpdateCycleFromLastPeriodAsync(Guid userId, DateOnly date, string status = CycleStatus.Ongoing)
    {
        var existing = await db.Cycles
            .FirstOrDefaultAsync(cycle => cycle.UserId == userId && cycle.StartDate == date);

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

    private static string InitialConsentMessage()
    {
        return "Oi! Eu sou sua assistente de ciclo pelo WhatsApp.\n\nAntes de começar: eu posso te ajudar a registrar menstruação, sintomas, lembretes e histórico. Não substituo orientação médica e não faço diagnósticos.\n\nPara continuar, preciso do seu consentimento para armazenar dados relacionados ao seu ciclo, sintomas e saúde menstrual.\n\nVocê aceita?\n1. Aceito\n2. Não aceito";
    }

    private static string MisunderstoodMessage(string hint)
    {
        return $"Não entendi sua resposta. Poderia tentar de novo, talvez de uma maneira mais direta?\n\n{hint}";
    }

    private static string CompletedOnboardingMessage(LumaUser user, OnboardingExtraction? captured = null)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "" : $"{user.DisplayName}, ";
        var intro = CountCapturedFields(captured) >= 2
            ? $"{name}pronto. Obrigada por já me passar tudo isso; seu cadastro inicial ficou completo."
            : $"{name}pronto. Seu cadastro inicial ficou completo.";

        return $"{intro}\n\nA partir de agora, pode falar comigo de um jeito bem natural. Por exemplo:\n\n\"menstruei hoje\"\n\"acabou ontem\"\n\"tô com cólica forte\"\n\"tive relação dia 20\"\n\"quando é minha próxima menstruação?\"\n\nEu vou te ajudar a organizar seus registros, sempre como estimativa e sem substituir orientação médica.";
    }

    private static string HelpMessage()
    {
        return "Você pode me mandar frases simples como:\n\n\"menstruei hoje\"\n\"acabou ontem\"\n\"fluxo leve\"\n\"tô com cólica forte\"\n\"minha menstruação está atrasada?\"\n\"quando é minha próxima menstruação?\"";
    }

    private static string GreetingMessage(LumaUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "" : $", {user.DisplayName}";
        return $"Oi{name}. Estou por aqui.\n\nVocê pode me mandar algo como:\n\n\"menstruei hoje\"\n\"acabou ontem\"\n\"tô com cólica forte\"\n\"quando é minha próxima menstruação?\"";
    }

    private static string BuildDelayReply(LumaUser user, DateOnly today)
    {
        var preference = EnsurePreference(user);
        if (preference.LastPeriodStartDate is null)
        {
            return "Ainda não tenho a data da sua última menstruação para calcular atraso. Você pode me dizer algo como \"menstruei dia 10/04\".";
        }

        var expected = preference.LastPeriodStartDate.Value.AddDays(preference.AverageCycleLength);
        var delayDays = today.DayNumber - expected.DayNumber;

        if (delayDays <= 0)
        {
            return $"Pela sua previsão atual, sua próxima menstruação está prevista para perto de {FormatDate(expected)}. Isso é só uma estimativa baseada nos seus registros.";
        }

        return $"Pela sua previsão atual, sua menstruação está cerca de {delayDays} dias atrasada.\n\nIsso pode acontecer por vários motivos, como variação natural do ciclo, estresse, alterações de rotina ou outros fatores. Se houver chance de gravidez ou sintomas preocupantes, o ideal é fazer um teste ou procurar orientação médica.";
    }

    private static string BuildNextPeriodReply(LumaUser user)
    {
        var preference = EnsurePreference(user);
        if (preference.LastPeriodStartDate is null)
        {
            return "Ainda não tenho a data da sua última menstruação para estimar a próxima. Você pode me dizer algo como \"menstruei dia 10/04\".";
        }

        var expected = preference.LastPeriodStartDate.Value.AddDays(preference.AverageCycleLength);
        return $"Pela sua média atual, sua próxima menstruação está prevista para perto de {FormatDate(expected)}. Essa é uma estimativa, não uma certeza.";
    }

    private static bool IsAffirmative(string body)
    {
        return body is "1" or "sim" or "s" or "aceito" or "aceitar"
            || body.Contains("aceito", StringComparison.Ordinal)
            || body.Contains("concordo", StringComparison.Ordinal);
    }

    private static bool IsNegative(string body)
    {
        return body is "2" or "nao" or "não" or "n"
            || body.Contains("nao aceito", StringComparison.Ordinal)
            || body.Contains("não aceito", StringComparison.Ordinal);
    }

    private static bool IsHelp(string body)
    {
        return body is "ajuda" or "help" or "menu";
    }

    private static bool IsGreeting(string body)
    {
        return body is "oi" or "ola" or "olá" or "bom dia" or "boa tarde" or "boa noite"
            || body.StartsWith("oi ", StringComparison.Ordinal)
            || body.StartsWith("ola ", StringComparison.Ordinal)
            || body.StartsWith("olá ", StringComparison.Ordinal);
    }

    private static bool IsPeriodStart(string body)
    {
        return body.Contains("menstruei", StringComparison.Ordinal)
            || body.Contains("menstruacao comecou", StringComparison.Ordinal)
            || body.Contains("menstruação começou", StringComparison.Ordinal)
            || body.Contains("comecou minha menstruacao", StringComparison.Ordinal)
            || body.Contains("desceu", StringComparison.Ordinal);
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
            || body.Contains("próxima", StringComparison.Ordinal)
            || body.Contains("quando", StringComparison.Ordinal) && body.Contains("menstru", StringComparison.Ordinal);
    }

    private static bool IsSexualActivity(string body)
    {
        return body.Contains("relacao", StringComparison.Ordinal)
            || body.Contains("relação", StringComparison.Ordinal)
            || body.Contains("sexo", StringComparison.Ordinal);
    }

    private static string? ParseFlowIntensity(string body)
    {
        if (body is "1" || body.Contains("leve", StringComparison.Ordinal))
        {
            return "light";
        }

        if (body is "2" || body.Contains("medio", StringComparison.Ordinal) || body.Contains("médio", StringComparison.Ordinal) || body.Contains("moderado", StringComparison.Ordinal))
        {
            return "medium";
        }

        if (body is "3" || body.Contains("intenso", StringComparison.Ordinal) || body.Contains("forte", StringComparison.Ordinal) || body.Contains("muito", StringComparison.Ordinal))
        {
            return "intense";
        }

        if (body is "4" || body.Contains("prefiro nao", StringComparison.Ordinal) || body.Contains("prefiro não", StringComparison.Ordinal))
        {
            return "unknown";
        }

        return null;
    }

    private static (string Key, string Label, string Intensity)? ParseSymptom(string body)
    {
        var intensity = body.Contains("forte", StringComparison.Ordinal) || body.Contains("absurda", StringComparison.Ordinal)
            ? "strong"
            : body.Contains("leve", StringComparison.Ordinal)
                ? "light"
                : "moderate";

        if (body.Contains("colica", StringComparison.Ordinal) || body.Contains("cólica", StringComparison.Ordinal))
        {
            return ("cramp", $"cólica {IntensityLabel(intensity)}", intensity);
        }

        if (body.Contains("dor de cabeca", StringComparison.Ordinal) || body.Contains("dor de cabeça", StringComparison.Ordinal))
        {
            return ("headache", $"dor de cabeça {IntensityLabel(intensity)}", intensity);
        }

        if (body.Contains("nausea", StringComparison.Ordinal) || body.Contains("náusea", StringComparison.Ordinal) || body.Contains("enjoo", StringComparison.Ordinal))
        {
            return ("nausea", $"náusea {IntensityLabel(intensity)}", intensity);
        }

        if (body.Contains("inchaco", StringComparison.Ordinal) || body.Contains("inchaço", StringComparison.Ordinal))
        {
            return ("bloating", $"inchaço {IntensityLabel(intensity)}", intensity);
        }

        if (body.Contains("dor lombar", StringComparison.Ordinal))
        {
            return ("back_pain", $"dor lombar {IntensityLabel(intensity)}", intensity);
        }

        return null;
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
            "medium" => "médio",
            "intense" => "intenso",
            _ => "não informado"
        };
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("dd/MM");
    }
}
