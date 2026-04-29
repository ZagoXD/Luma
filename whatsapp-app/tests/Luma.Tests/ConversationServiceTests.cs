using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Luma.Tests;

public sealed class ConversationServiceTests
{
    [Fact]
    public async Task Name_step_discards_unsafe_ai_age_inference()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(message =>
            message.Contains("Nayara", StringComparison.OrdinalIgnoreCase)
                ? new OnboardingExtraction { DisplayName = "Nayara", IsAdultConfirmed = false }
                : null));

        var phone = "+5516992000001";
        await SendAsync(service, phone, "Olá");
        await SendAsync(service, phone, "Aceito");
        var reply = await SendAsync(service, phone, "Pode me chamar de Nayara");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Equal("Nayara", user.DisplayName);
        Assert.Equal(OnboardingSteps.AwaitingAgeConfirmation, user.OnboardingStep);
        Assert.Null(user.IsAdultConfirmed);
        Assert.Contains("confirmar se tem 18 anos", reply);
    }

    [Fact]
    public async Task Name_step_does_not_save_unrecognized_sentence_as_display_name()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000002";
        await SendAsync(service, phone, "Olá");
        await SendAsync(service, phone, "Aceito");
        var reply = await SendAsync(service, phone, "meu ciclo costuma ter 29 dias");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Null(user.DisplayName);
        Assert.Equal(OnboardingSteps.AwaitingDisplayName, user.OnboardingStep);
        Assert.Contains("Não entendi sua resposta", reply);
    }

    [Fact]
    public async Task Consent_accepts_natural_affirmative_answer()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000053";
        await SendAsync(service, phone, "Ola");
        var reply = await SendAsync(service, phone, "Claro!");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Equal(OnboardingSteps.AwaitingDisplayName, user.OnboardingStep);
        Assert.Contains("como devo te chamar", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Agent_can_accept_consent_without_deterministic_affirmative_word()
    {
        await using var db = CreateDbContext();
        var agent = new FakeToolAgent(request =>
            request.UserMessage.Contains("perfeitamente", StringComparison.OrdinalIgnoreCase)
                ? new LumaToolCall
                {
                    ToolName = "complete_onboarding_step",
                    ConsentAccepted = true,
                    Confidence = 0.95
                }
                : null);
        var service = CreateService(db, new FakeExtractor(_ => null), toolAgent: agent);

        var phone = "+5516992000062";
        await SendAsync(service, phone, "Ola");
        var reply = await SendAsync(service, phone, "perfeitamente, pode seguir");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Equal(OnboardingSteps.AwaitingDisplayName, user.OnboardingStep);
        Assert.Contains("como devo te chamar", MessageText.Normalize(reply));
        Assert.Contains(agent.Requests, request => request.Context.OnboardingStep == OnboardingSteps.AwaitingConsent);
    }

    [Fact]
    public async Task Agent_cannot_turn_plain_greeting_into_consent()
    {
        await using var db = CreateDbContext();
        var agent = new FakeToolAgent(_ => new LumaToolCall
        {
            ToolName = "complete_onboarding_step",
            ConsentAccepted = true,
            Confidence = 0.95
        });
        var service = CreateService(db, new FakeExtractor(_ => null), toolAgent: agent);

        await SendAsync(service, "+5516992000064", "Ola, tudo bem?");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000064");
        Assert.Equal(OnboardingSteps.AwaitingConsent, user.OnboardingStep);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task Agent_tool_call_records_completed_user_period_start_with_unmapped_wording()
    {
        await using var db = CreateDbContext();
        var agent = new FakeToolAgent(request =>
            request.UserMessage.Contains("visita mensal", StringComparison.OrdinalIgnoreCase)
                ? new LumaToolCall
                {
                    ToolName = "record_period_start",
                    Date = new DateOnly(2026, 4, 25),
                    Confidence = 0.96
                }
                : null);
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000063", toolAgent: agent);

        var reply = await SendAsync(service, "+5516992000063", "a visita mensal apareceu");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000063");
        var ev = await db.CycleEvents
            .Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.PeriodStart)
            .OrderByDescending(ev => ev.CreatedAt)
            .FirstAsync();
        Assert.Equal(new DateOnly(2026, 4, 25), ev.Date);
        Assert.Contains("agent_tool", ev.MetadataJson);
        Assert.Contains("inicio da sua menstruacao", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Non_guardrail_reply_is_written_by_luma_ai_response_generator()
    {
        await using var db = CreateDbContext();
        var responseGenerator = new FakeResponseGenerator(_ => "Oi, eu sou a Luma em modo producao. Pode me chamar do seu jeito.");
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000060", responseGenerator: responseGenerator);

        responseGenerator.Requests.Clear();
        var reply = await SendAsync(service, "+5516992000060", "Ola");

        Assert.Equal("Oi, eu sou a Luma em modo producao. Pode me chamar do seu jeito.", reply);
        Assert.Single(responseGenerator.Requests);
        Assert.Contains(responseGenerator.Requests[0].AvailableTools, tool => tool.StartsWith("get_onboarding_state", StringComparison.Ordinal));
        Assert.Equal(OnboardingSteps.Completed, responseGenerator.Requests[0].OnboardingStep);
    }

    [Fact]
    public async Task Required_onboarding_prompt_does_not_go_through_luma_ai_response_generator()
    {
        await using var db = CreateDbContext();
        var responseGenerator = new FakeResponseGenerator(_ => "isso deixaria o webhook lento");
        var service = CreateService(db, new FakeExtractor(_ => null), responseGenerator: responseGenerator);

        var reply = await SendAsync(service, "+5516992000065", "Ola");

        Assert.Contains("voce aceita", MessageText.Normalize(reply));
        Assert.Empty(responseGenerator.Requests);
    }

    [Fact]
    public async Task Fixed_medical_guardrail_does_not_go_through_luma_ai_response_generator()
    {
        await using var db = CreateDbContext();
        var responseGenerator = new FakeResponseGenerator(_ => "isso nao deveria aparecer");
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000061", responseGenerator: responseGenerator);

        responseGenerator.Requests.Clear();
        var reply = await SendAsync(service, "+5516992000061", "Estou gravida?");

        Assert.Contains("nao consigo confirmar", MessageText.Normalize(reply));
        Assert.Empty(responseGenerator.Requests);
    }

    [Fact]
    public async Task Display_name_step_can_capture_name_and_age_in_same_message()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000054";
        await SendAsync(service, phone, "Ola");
        await SendAsync(service, phone, "Aceito");
        var reply = await SendAsync(service, phone, "Voce pode me chamar de Nay, e eu tenho 21 anos");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Equal("Nay", user.DisplayName);
        Assert.True(user.IsAdultConfirmed);
        Assert.Equal(OnboardingSteps.AwaitingLastPeriodStart, user.OnboardingStep);
        Assert.Contains("qual foi o primeiro dia da sua ultima menstruacao", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Last_period_step_saves_relative_date_and_event()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000003";
        await SendAsync(service, phone, "Olá");
        await SendAsync(service, phone, "Aceito");
        await SendAsync(service, phone, "Nay");
        await SendAsync(service, phone, "Sim, tenho 23 anos");
        await SendAsync(service, phone, "começou há uns 5 dias");

        var user = await db.Users.Include(user => user.Preference).SingleAsync(user => user.PhoneNumber == phone);
        var periodStart = await db.CycleEvents.SingleAsync(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.PeriodStart);
        Assert.Equal(new DateOnly(2026, 4, 20), user.Preference!.LastPeriodStartDate);
        Assert.Equal(new DateOnly(2026, 4, 20), periodStart.Date);
    }

    [Fact]
    public async Task Onboarding_saves_out_of_order_period_start_as_pending_intent_and_confirms_after_completion()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000050";
        await SendAsync(service, phone, "Ola");
        await SendAsync(service, phone, "Aceito");
        var pendingReply = await SendAsync(service, phone, "menstruei hoje");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        var pending = Assert.Single(await db.PendingIntents.Where(intent => intent.UserId == user.Id).ToListAsync());
        Assert.Equal(OnboardingSteps.AwaitingDisplayName, user.OnboardingStep);
        Assert.Equal(ConversationIntents.PeriodStart, pending.Intent);
        Assert.Equal(new DateOnly(2026, 4, 25), pending.Date);
        Assert.Contains("ja vi que voce quer registrar o inicio da menstruacao hoje", MessageText.Normalize(pendingReply));
        Assert.Empty(await db.CycleEvents.Where(ev => ev.UserId == user.Id).ToListAsync());

        await SendAsync(service, phone, "Julia");
        await SendAsync(service, phone, "Sim, tenho 25 anos");
        await SendAsync(service, phone, "nao lembro");
        await SendAsync(service, phone, "28 dias");
        await SendAsync(service, phone, "5 dias");
        var completedReply = await SendAsync(service, phone, "Prefiro nao informar");

        Assert.Contains("voce tinha me contado que sua menstruacao comecou hoje", MessageText.Normalize(completedReply));
        Assert.Contains("quer que eu registre isso agora", MessageText.Normalize(completedReply));
        Assert.Empty(await db.CycleEvents.Where(ev => ev.UserId == user.Id).ToListAsync());

        var confirmationReply = await SendAsync(service, phone, "sim");

        var periodStart = Assert.Single(await db.CycleEvents.Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.PeriodStart).ToListAsync());
        Assert.Equal(new DateOnly(2026, 4, 25), periodStart.Date);
        Assert.Contains("registrei o inicio da sua menstruacao em 25/04", MessageText.Normalize(confirmationReply));
        Assert.All(await db.PendingIntents.Where(intent => intent.UserId == user.Id).ToListAsync(), intent => Assert.Equal(PendingIntentStatus.Completed, intent.Status));
    }

    [Fact]
    public async Task Onboarding_saves_ai_detected_unmapped_intent_as_pending_intent()
    {
        await using var db = CreateDbContext();
        var service = CreateService(
            db,
            new FakeExtractor(_ => null),
            new FakeIntentExtractor(message =>
                message.Contains("intimo", StringComparison.OrdinalIgnoreCase)
                    ? new ConversationIntent
                    {
                        Intent = ConversationIntents.SexualActivity,
                        Date = new DateOnly(2026, 4, 24),
                        Protected = "unknown",
                        Confidence = 0.92
                    }
                    : null));

        var phone = "+5516992000051";
        await SendAsync(service, phone, "Ola");
        await SendAsync(service, phone, "Aceito");
        var reply = await SendAsync(service, phone, "Ontem ficamos de um jeito mais intimo");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        var pending = Assert.Single(await db.PendingIntents.Where(intent => intent.UserId == user.Id).ToListAsync());
        Assert.Equal(ConversationIntents.SexualActivity, pending.Intent);
        Assert.Equal(new DateOnly(2026, 4, 24), pending.Date);
        Assert.Contains("quer registrar uma relacao", MessageText.Normalize(reply));
        Assert.Contains("preciso terminar seu cadastro", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_can_ask_privacy_question_from_luma_knowledge_base()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000052");

        var reply = await SendAsync(service, "+5516992000052", "Luma, como voce protege meus dados?");

        var normalized = MessageText.Normalize(reply);
        Assert.Contains("privacidade", normalized);
        Assert.Contains("consentimento", normalized);
        Assert.Contains("apagar", normalized);
    }

    [Theory]
    [InlineData("Tomo pilula anticoncepcional", true, "pill")]
    [InlineData("Uso DIU hormonal", true, "hormonal_iud")]
    [InlineData("Uso camisinha", false, "condom")]
    [InlineData("Nao uso nenhum metodo", false, "none")]
    [InlineData("Prefiro nao informar", false, "prefer_not_say")]
    public async Task Onboarding_collects_optional_contraceptive_method(string answer, bool usesHormonal, string expectedType)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000010";
        var prompt = await CompleteBasicOnboardingUntilContraceptiveAsync(service, phone);
        Assert.Contains("metodo contraceptivo", MessageText.Normalize(prompt));

        var reply = await SendAsync(service, phone, answer);

        var user = await db.Users.Include(user => user.Preference).SingleAsync(user => user.PhoneNumber == phone);
        Assert.Equal(OnboardingSteps.Completed, user.OnboardingStep);
        Assert.Equal(usesHormonal, user.Preference!.UsesHormonalContraceptive);
        Assert.Equal(expectedType, user.Preference.ContraceptiveType);
        Assert.Contains("cadastro inicial ficou completo", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_records_period_start_with_flow_from_natural_message()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000020");

        var reply = await SendAsync(service, "+5516992000020", "Desceu ontem e hoje ta vindo muito forte");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000020");
        var cycle = await db.Cycles.SingleAsync(cycle => cycle.UserId == user.Id && cycle.Status == CycleStatus.Ongoing);
        var events = await db.CycleEvents.Where(ev => ev.UserId == user.Id).OrderBy(ev => ev.Type).ToListAsync();

        Assert.Equal(new DateOnly(2026, 4, 24), cycle.StartDate);
        Assert.Contains(events, ev => ev.Type == CycleEventTypes.PeriodStart && ev.Date == new DateOnly(2026, 4, 24));
        var flow = Assert.Single(events.Where(ev => ev.Type == CycleEventTypes.FlowUpdate));
        Assert.Equal("intense", JsonDocument.Parse(flow.MetadataJson).RootElement.GetProperty("flow_intensity").GetString());
        Assert.Contains("fluxo intenso", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_finishes_open_cycle_and_reports_duration_and_next_period()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000021");

        await SendAsync(service, "+5516992000021", "menstruei dia 20/04");
        var reply = await SendAsync(service, "+5516992000021", "acabou ontem");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000021");
        var cycle = await db.Cycles.SingleAsync(cycle => cycle.UserId == user.Id && cycle.StartDate == new DateOnly(2026, 4, 20));
        Assert.Equal(CycleStatus.Finished, cycle.Status);
        Assert.Equal(new DateOnly(2026, 4, 24), cycle.EndDate);
        Assert.Contains("durou cerca de 5 dias", MessageText.Normalize(reply));
        Assert.Contains("prevista para perto de 18/05", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_rejects_period_end_before_start()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000022");

        await SendAsync(service, "+5516992000022", "menstruei dia 20/04");
        var reply = await SendAsync(service, "+5516992000022", "acabou dia 18/04");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000022");
        var cycle = await db.Cycles.SingleAsync(cycle => cycle.UserId == user.Id && cycle.StartDate == new DateOnly(2026, 4, 20));
        Assert.Equal(CycleStatus.Ongoing, cycle.Status);
        Assert.Null(cycle.EndDate);
        Assert.Contains("data de termino ficou antes do inicio", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_keeps_one_flow_update_per_day()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000023");

        await SendAsync(service, "+5516992000023", "fluxo medio");
        var reply = await SendAsync(service, "+5516992000023", "hoje esta bem intenso");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000023");
        var flow = Assert.Single(await db.CycleEvents.Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.FlowUpdate).ToListAsync());
        Assert.Equal("intense", JsonDocument.Parse(flow.MetadataJson).RootElement.GetProperty("flow_intensity").GetString());
        Assert.Contains("atualizei", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_records_multiple_symptoms_and_warns_for_red_flags()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000024");

        var reply = await SendAsync(service, "+5516992000024", "Hoje estou com colica absurda e nausea");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000024");
        var symptoms = await db.CycleEvents.Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.Symptom).ToListAsync();
        Assert.Equal(2, symptoms.Count);
        Assert.Contains(symptoms, ev => JsonDocument.Parse(ev.MetadataJson).RootElement.GetProperty("symptom").GetString() == "cramp");
        Assert.Contains(symptoms, ev => JsonDocument.Parse(ev.MetadataJson).RootElement.GetProperty("symptom").GetString() == "nausea");
        Assert.Contains("procure orientacao medica", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_records_mood_without_diagnosis()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000025");

        var reply = await SendAsync(service, "+5516992000025", "Hoje estou irritada e sensivel");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000025");
        var mood = Assert.Single(await db.CycleEvents.Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.Mood).ToListAsync());
        Assert.Equal("irritable", JsonDocument.Parse(mood.MetadataJson).RootElement.GetProperty("mood").GetString());
        Assert.DoesNotContain("tpm", MessageText.Normalize(reply));
        Assert.Contains("registre", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_records_sexual_activity_with_protection_metadata()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000026");

        var reply = await SendAsync(service, "+5516992000026", "Tive relacao dia 20 com camisinha");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000026");
        var ev = Assert.Single(await db.CycleEvents.Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.SexualActivity).ToListAsync());
        var metadata = JsonDocument.Parse(ev.MetadataJson).RootElement;
        Assert.Equal(new DateOnly(2026, 4, 20), ev.Date);
        Assert.Equal("yes", metadata.GetProperty("protected").GetString());
        Assert.Contains("nao uso isso para afirmar gravidez", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_records_sexual_activity_from_ai_intent_when_text_uses_unmapped_wording()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(
            db,
            "+5516992000030",
            new FakeIntentExtractor(message =>
                message.Contains("ficamos", StringComparison.OrdinalIgnoreCase)
                    ? new ConversationIntent { Intent = ConversationIntents.SexualActivity, Date = new DateOnly(2026, 4, 24), Protected = "unknown" }
                    : null));

        var reply = await SendAsync(service, "+5516992000030", "Ontem ficamos de um jeito mais intimo");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000030");
        var ev = Assert.Single(await db.CycleEvents.Where(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.SexualActivity).ToListAsync());
        Assert.Equal(new DateOnly(2026, 4, 24), ev.Date);
        Assert.Contains("registrei", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Completed_user_answers_last_sexual_activity_question()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000031");

        await SendAsync(service, "+5516992000031", "tive relacao sexual ontem com camisinha");
        var reply = await SendAsync(service, "+5516992000031", "Luma, quando foi minha ultima relacao sexual?");

        Assert.Contains("ultima relacao registrada foi em 24/04", MessageText.Normalize(reply));
        Assert.Contains("historico", MessageText.Normalize(reply));
    }

    [Theory]
    [InlineData("quando e minha proxima menstruacao?", "prevista para perto de 23/05")]
    [InlineData("minha menstruacao esta atrasada?", "ainda nao parece atrasada")]
    [InlineData("quando foi minha ultima menstruacao?", "ultima menstruacao registrada foi em 25/04")]
    [InlineData("qual foi meu ultimo sintoma registrado?", "ainda nao tenho sintomas registrados")]
    public async Task Completed_user_answers_basic_history_and_calculation_questions(string question, string expected)
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000027");

        var reply = await SendAsync(service, "+5516992000027", question);

        Assert.Contains(expected, MessageText.Normalize(reply));
    }

    [Theory]
    [InlineData("Estou gravida?")]
    [InlineData("Esse sangramento e normal?")]
    [InlineData("Posso ter relacao sem protecao hoje?")]
    [InlineData("Estou no periodo seguro?")]
    public async Task Completed_user_blocks_medical_or_unsafe_claims(string message)
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000028");
        var beforeCount = await db.CycleEvents.CountAsync();

        var reply = await SendAsync(service, "+5516992000028", message);

        Assert.Contains("nao consigo confirmar", MessageText.Normalize(reply));
        Assert.Equal(beforeCount, await db.CycleEvents.CountAsync());
    }

    [Fact]
    public async Task Completed_user_can_ask_who_luma_is()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000040");

        var reply = await SendAsync(service, "+5516992000040", "Luma, quem e voce?");

        var normalized = MessageText.Normalize(reply);
        Assert.Contains("sou a luma", normalized);
        Assert.Contains("ciclo menstrual", normalized);
        Assert.Contains("gravidez", normalized);
        Assert.Contains("nao faco diagnosticos", normalized);
    }

    [Fact]
    public async Task Completed_user_gets_scope_reply_for_unrelated_questions()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000041");

        var reply = await SendAsync(service, "+5516992000041", "Qual investimento devo comprar hoje?");

        var normalized = MessageText.Normalize(reply);
        Assert.Contains("nao posso opinar sobre isso", normalized);
        Assert.Contains("ciclo menstrual", normalized);
    }

    [Fact]
    public async Task Pregnancy_start_creates_active_pregnancy_and_asks_for_reference()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000042");

        var reply = await SendAsync(service, "+5516992000042", "Descobri que estou gravida");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000042");
        var pregnancy = Assert.Single(await db.Pregnancies.Where(pregnancy => pregnancy.UserId == user.Id).ToListAsync());
        Assert.Equal(PregnancyStatus.Active, pregnancy.Status);
        Assert.Equal(PendingActions.AwaitingPregnancyReference, user.PendingAction);
        Assert.Contains("data da ultima menstruacao", MessageText.Normalize(reply));
        Assert.Contains(await db.CycleEvents.Where(ev => ev.UserId == user.Id).ToListAsync(), ev => ev.Type == CycleEventTypes.PregnancyPositive);
    }

    [Fact]
    public async Task Pregnancy_start_with_weeks_calculates_estimated_due_date()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000043");

        var reply = await SendAsync(service, "+5516992000043", "Estou gravida de 8 semanas");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000043");
        var pregnancy = Assert.Single(await db.Pregnancies.Where(pregnancy => pregnancy.UserId == user.Id).ToListAsync());
        Assert.Equal(8, pregnancy.GestationalWeeksAtRegistration);
        Assert.Equal(new DateOnly(2026, 2, 28), pregnancy.LastPeriodDate);
        Assert.Equal(new DateOnly(2026, 12, 5), pregnancy.EstimatedDueDate);
        Assert.Contains("8 semanas", MessageText.Normalize(reply));
        Assert.Contains("05/12", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Pregnancy_pending_reference_accepts_last_period_and_calculates_due_date()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000044");

        await SendAsync(service, "+5516992000044", "Meu teste deu positivo");
        var reply = await SendAsync(service, "+5516992000044", "Minha ultima menstruacao foi dia 01/03");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000044");
        var pregnancy = Assert.Single(await db.Pregnancies.Where(pregnancy => pregnancy.UserId == user.Id).ToListAsync());
        Assert.Equal(new DateOnly(2026, 3, 1), pregnancy.LastPeriodDate);
        Assert.Equal(new DateOnly(2026, 12, 6), pregnancy.EstimatedDueDate);
        Assert.Null(user.PendingAction);
        Assert.Contains("06/12", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Pregnancy_bleeding_uses_fixed_guardrail_and_records_event()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000045");

        await SendAsync(service, "+5516992000045", "Estou gravida de 8 semanas");
        var reply = await SendAsync(service, "+5516992000045", "Tive sangramento hoje");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000045");
        Assert.Contains(await db.CycleEvents.Where(ev => ev.UserId == user.Id).ToListAsync(), ev => ev.Type == CycleEventTypes.PregnancyBleeding);
        Assert.Contains("sangramentos na gravidez", MessageText.Normalize(reply));
        Assert.Contains("obstetra", MessageText.Normalize(reply));
    }

    [Fact]
    public async Task Pregnancy_records_symptom_prenatal_appointment_and_ultrasound()
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000046");

        await SendAsync(service, "+5516992000046", "Estou gravida de 8 semanas");
        await SendAsync(service, "+5516992000046", "Hoje estou com muita nausea");
        await SendAsync(service, "+5516992000046", "Tenho consulta pre natal dia 30/04");
        await SendAsync(service, "+5516992000046", "Fiz ultrassom ontem");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == "+5516992000046");
        var events = await db.CycleEvents.Where(ev => ev.UserId == user.Id).ToListAsync();
        Assert.Contains(events, ev => ev.Type == CycleEventTypes.PregnancySymptom);
        Assert.Contains(events, ev => ev.Type == CycleEventTypes.PrenatalAppointment && ev.Date == new DateOnly(2026, 4, 30));
        Assert.Contains(events, ev => ev.Type == CycleEventTypes.Ultrasound && ev.Date == new DateOnly(2026, 4, 24));
    }

    [Theory]
    [InlineData("de quantas semanas estou?", "8 semanas")]
    [InlineData("qual minha data provavel do parto?", "05/12")]
    public async Task Pregnancy_answers_week_and_due_date_questions(string question, string expected)
    {
        await using var db = CreateDbContext();
        var service = await CreateCompletedUserServiceAsync(db, "+5516992000047");

        await SendAsync(service, "+5516992000047", "Estou gravida de 8 semanas");
        var reply = await SendAsync(service, "+5516992000047", question);

        Assert.Contains(expected, MessageText.Normalize(reply));
        Assert.Contains("estimativa", MessageText.Normalize(reply));
    }

    private static LumaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LumaDbContext(options);
    }

    private static ConversationService CreateService(
        LumaDbContext db,
        IOnboardingDataExtractor extractor,
        IConversationIntentExtractor? intentExtractor = null,
        ILumaToolAgent? toolAgent = null,
        ILumaResponseGenerator? responseGenerator = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Luma:StoreMessageBodies"] = "false"
            })
            .Build();

        return new ConversationService(
            db,
            configuration,
            extractor,
            intentExtractor ?? new FakeIntentExtractor(_ => null),
            toolAgent ?? new NullLumaToolAgent(),
            responseGenerator ?? new PassthroughLumaResponseGenerator(),
            new FixedDateProvider(new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ConversationService>.Instance);
    }

    private static Task<string> SendAsync(ConversationService service, string phone, string body)
    {
        return service.HandleIncomingMessageAsync(new IncomingMessage("test", phone, body, null));
    }

    private static async Task<string> CompleteBasicOnboardingUntilContraceptiveAsync(ConversationService service, string phone)
    {
        await SendAsync(service, phone, "Ola");
        await SendAsync(service, phone, "Aceito");
        await SendAsync(service, phone, "Julia");
        await SendAsync(service, phone, "Sim, tenho 25 anos");
        await SendAsync(service, phone, "minha ultima menstruacao foi hoje");
        await SendAsync(service, phone, "Meu ciclo costuma ter 28 dias");
        return await SendAsync(service, phone, "Costuma durar 5 dias");
    }

    private static async Task<ConversationService> CreateCompletedUserServiceAsync(
        LumaDbContext db,
        string phone,
        IConversationIntentExtractor? intentExtractor = null,
        ILumaToolAgent? toolAgent = null,
        ILumaResponseGenerator? responseGenerator = null)
    {
        var service = CreateService(db, new FakeExtractor(_ => null), intentExtractor, toolAgent, responseGenerator);
        await CompleteBasicOnboardingUntilContraceptiveAsync(service, phone);
        await SendAsync(service, phone, "Prefiro nao informar");
        db.ChangeTracker.Clear();
        return service;
    }

    private sealed class FakeExtractor(Func<string, OnboardingExtraction?> extract) : IOnboardingDataExtractor
    {
        public Task<OnboardingExtraction?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(extract(message));
        }
    }

    private sealed class FakeIntentExtractor(Func<string, ConversationIntent?> extract) : IConversationIntentExtractor
    {
        public Task<ConversationIntent?> ExtractAsync(
            string message,
            DateOnly today,
            ConversationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(extract(message));
        }
    }

    private sealed class FakeToolAgent(Func<LumaToolAgentRequest, LumaToolCall?> decide) : ILumaToolAgent
    {
        public List<LumaToolAgentRequest> Requests { get; } = [];

        public Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(decide(request));
        }
    }

    private sealed class FakeResponseGenerator(Func<LumaResponseRequest, string> generate) : ILumaResponseGenerator
    {
        public List<LumaResponseRequest> Requests { get; } = [];

        public Task<string> GenerateAsync(LumaResponseRequest request, CancellationToken cancellationToken = default)
        {
            if (!request.IsGuardrail)
            {
                Requests.Add(request);
            }

            return Task.FromResult(request.IsGuardrail ? request.BackendResult : generate(request));
        }
    }

    private sealed class FixedDateProvider(DateTimeOffset utcNow) : IDateProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
