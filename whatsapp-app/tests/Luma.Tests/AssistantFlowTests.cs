using System.Text.Json;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Luma.Api.Services.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Luma.Tests;

public sealed class AssistantFlowTests
{
    [PostgresFact]
    public async Task Concurrent_webhook_replays_are_serialized_in_postgres()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")).Options;
        await using var setup = new LumaDbContext(options);
        var connection = setup.Database.GetDbConnection();
        await connection.OpenAsync();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT to_regclass('users') IS NULL";
            if ((bool)(await check.ExecuteScalarAsync())!)
                await setup.Database.ExecuteSqlRawAsync(setup.Database.GenerateCreateScript());
        }
        var phone = "+55169" + Random.Shared.Next(10000000, 99999999);
        var user = new LumaUser
        {
            PhoneNumber = phone,
            OnboardingStep = OnboardingSteps.Completed,
            IsAdultConfirmed = true,
            ConsentAcceptedAt = DateTimeOffset.UtcNow,
            Preference = new()
        };
        setup.Users.Add(user);
        await setup.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("record_period_start", new { date = "2026-11-09" });
        var message = new IncomingMessage("concurrency", phone, "menstruei ontem", Guid.NewGuid().ToString());
        await using var db1 = new LumaDbContext(options);
        await using var db2 = new LumaDbContext(options);
        var replies = await Task.WhenAll(CreateService(db1, model).HandleIncomingMessageRichAsync(message),
            CreateService(db2, model).HandleIncomingMessageRichAsync(message));
        Assert.Equal(replies[0], replies[1]);
        Assert.Equal(1, await setup.Cycles.CountAsync(cycle => cycle.UserId == user.Id));
        Assert.Equal(1, await setup.AssistantTurns.CountAsync(turn => turn.UserId == user.Id));
    }

    [Fact]
    public async Task Profile_tool_reads_only_the_current_users_valid_plan()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        db.AccountSubscriptions.AddRange(new AccountSubscription
        {
            PhoneNumber = user.PhoneNumber,
            PlanCode = "essencial",
            Status = SubscriptionStatuses.Active,
            BillingInterval = "annual",
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30)
        },
            new AccountSubscription { PhoneNumber = "+5516999998888", PlanCode = "other-user-private-plan" });
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("get_user_profile", new { });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "qual meu plano?", "plan"));
        Assert.Contains("essencial", model.LastInput);
        Assert.Contains("annual", model.LastInput);
        Assert.DoesNotContain("other-user-private-plan", model.LastInput);
    }

    [Fact]
    public async Task Onboarding_accepts_informal_age_and_explicit_unknown_date()
    {
        await using var db = CreateDb();
        var user = await AddUser(db, OnboardingSteps.AwaitingAgeConfirmation);
        user.IsAdultConfirmed = null;
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("complete_onboarding_step", new { is_adult_confirmed = true });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "ss", "age"));
        Assert.True(user.IsAdultConfirmed);
        Assert.Equal(OnboardingSteps.AwaitingLastPeriodStart, user.OnboardingStep);
        model.Call("complete_onboarding_step", new { last_period_unknown = true });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "não lembro a data", "date"));
        Assert.Equal(OnboardingSteps.AwaitingAverageCycleLength, user.OnboardingStep);
        Assert.Null(user.Preference!.LastPeriodStartDate);
    }

    [Fact]
    public async Task Pregnancy_due_date_is_a_supported_tool_and_has_no_medical_refusal()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        var model = new ScriptedModel { FailAfterCall = true };
        model.Call("start_pregnancy_mode", new { gestational_weeks = 7 });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "estou grávida de 7 semanas", "pregnancy"));
        Assert.Single(await db.Pregnancies.ToListAsync());
        model.Call("get_pregnancy_due_date", new { });
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "previsão do parto?", "due-date"));
        Assert.DoesNotContain(SafetyGuardrail.SafeReply, reply);
        Assert.Contains("data provavel do parto", MessageText.Normalize(reply));
    }

    [Theory]
    [InlineData("medical_guardrail")]
    [InlineData("out_of_scope")]
    public async Task Guardrail_tools_do_not_write_events(string tool)
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        var model = new ScriptedModel();
        model.Call(tool, new { });
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "pergunta", tool));
        Assert.Empty(await db.CycleEvents.ToListAsync());
        Assert.Equal(1, model.Requests);
        Assert.NotEmpty(reply);
    }

    [Fact]
    public async Task Real_mcp_path_records_once_and_replays_webhook_receipt()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        var model = new ScriptedModel();
        model.Call("record_period_start", new { date = "2026-11-09" });
        var service = CreateService(db, model);
        var message = new IncomingMessage("test", user.PhoneNumber, "desceu ontem", "message-1");
        var first = await service.HandleIncomingMessageRichAsync(message);
        var second = await service.HandleIncomingMessageRichAsync(message);
        Assert.Equal(first, second);
        Assert.Single(await db.Cycles.ToListAsync());
        Assert.Single(await db.CycleEvents.Where(item => item.Type == CycleEventTypes.PeriodStart).ToListAsync());
        Assert.Equal(new DateOnly(2026, 11, 9), user.Preference!.LastPeriodStartDate);
        Assert.Equal(1, model.Requests);
    }

    [Fact]
    public async Task Preference_update_does_not_end_an_ongoing_period()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        db.Cycles.Add(new Cycle { UserId = user.Id, StartDate = new(2026, 11, 9), Status = CycleStatus.Ongoing });
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("update_user_preferences", new { average_period_length = 3 });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "costuma durar três dias", "preference"));
        Assert.Equal(3, user.Preference!.AveragePeriodLength);
        Assert.Null((await db.Cycles.SingleAsync()).EndDate);
        Assert.Empty(await db.CycleEvents.ToListAsync());
    }

    [Fact]
    public async Task Out_of_order_intent_waits_for_confirmation_and_keeps_original_date()
    {
        await using var db = CreateDb();
        var user = await AddUser(db, OnboardingSteps.AwaitingDisplayName);
        var model = new ScriptedModel();
        model.Call("record_period_start", new { date = "2026-11-09" });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "menstruei ontem", "pending"));
        Assert.Empty(await db.Cycles.ToListAsync());
        Assert.Single(await db.PendingIntents.ToListAsync());
        user.OnboardingStep = OnboardingSteps.Completed;
        await db.SaveChangesAsync();
        model.Call("confirm_pending_intent", new { confirmed = true });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "pode registrar", "confirm"));
        Assert.Equal(new DateOnly(2026, 11, 9), (await db.Cycles.SingleAsync()).StartDate);
        Assert.Equal(PendingIntentStatus.Completed, (await db.PendingIntents.SingleAsync()).Status);
    }

    [Fact]
    public async Task Health_data_is_not_written_or_kept_in_memory_before_consent()
    {
        await using var db = CreateDb();
        var user = await AddUser(db, OnboardingSteps.AwaitingConsent);
        user.ConsentAcceptedAt = null;
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("record_period_start", new { date = "2026-11-09" });
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "menstruei ontem", "no-consent"));
        Assert.Contains("consentimento", reply);
        Assert.Empty(await db.Cycles.ToListAsync());
        Assert.Empty(await db.PendingIntents.ToListAsync());
        Assert.Equal("", (await db.AssistantTurns.SingleAsync()).Request);
    }

    [Fact]
    public async Task Essential_plan_saves_independent_reminder_times_through_mcp()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            PhoneNumber = user.PhoneNumber,
            PlanCode = "essencial",
            Status = SubscriptionStatuses.Active,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("update_notification_preferences", new
        {
            contraceptive_type = "pill",
            period_reminder_enabled = true,
            contraceptive_reminder_enabled = true,
            symptom_checkin_enabled = true,
            period_reminder_time = "09:30",
            contraceptive_reminder_time = "07:00",
            symptom_checkin_time = "20:00"
        });
        await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "quero os lembretes", "essential"));
        var preference = await db.NotificationPreferences.SingleAsync();
        Assert.Equal(new TimeOnly(9, 30), preference.PeriodReminderTime);
        Assert.Equal(new TimeOnly(7, 0), preference.ContraceptiveReminderTime);
        Assert.Equal(new TimeOnly(20, 0), preference.SymptomCheckinTime);
        Assert.Equal("pill", user.Preference!.ContraceptiveType);
    }

    [Fact]
    public async Task Basic_plan_cannot_enable_notifications()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        var model = new ScriptedModel();
        model.Call("update_notification_preferences", new { contraceptive_reminder_enabled = true, contraceptive_reminder_time = "07:00" });
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "quero lembrete", "basic"));
        Assert.Contains("Essencial", reply);
        Assert.Empty(await db.NotificationPreferences.ToListAsync());
    }

    [Fact]
    public async Task Model_failure_after_write_returns_real_result_without_repeating_the_write()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        var model = new ScriptedModel { FailAfterCall = true };
        model.Call("record_sexual_activity", new { date = "2026-11-09" });
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "tive relação ontem", "failure"));
        Assert.Contains("09/11", reply);
        Assert.Single(await db.CycleEvents.ToListAsync());
    }

    [Fact]
    public async Task Exhausted_AI_quota_still_allows_records_corrections_and_replay_without_model_calls()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LumaDbContext(options);
        var user = await AddUser(db);
        var account = new AccountUser { PhoneNumber = user.PhoneNumber, Email = "test@example.invalid" };
        db.AccountUsers.Add(account);
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            AccountUserId = account.Id,
            PhoneNumber = user.PhoneNumber,
            PlanCode = "basico",
            Status = SubscriptionStatuses.Active,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
        var ledger = new UsageLedger(options, Options.Create(new UsageOptions { BasicDailyMessages = 1 }), TimeProvider.System);
        var model = new ScriptedModel();
        model.Call("get_last_period", new { });
        var service = CreateService(db, model, ledger, new UsageContext());
        await service.HandleIncomingMessageAsync(new("test", user.PhoneNumber, "qual minha última menstruação?", "quota-1"));
        var calls = model.Requests;
        var blocked = await service.HandleIncomingMessageAsync(new("test", user.PhoneNumber, "outra pergunta", "quota-2"));
        Assert.Contains("limite", blocked);
        Assert.Equal(calls, model.Requests);
        Assert.Contains("1 de 1", await service.HandleIncomingMessageAsync(new("test", user.PhoneNumber, "meu uso", "usage-without-ai")));
        Assert.Equal(calls, model.Requests);
        foreach (var text in new[] { "registrar", "1", "ontem", "1" })
            await service.HandleIncomingMessageAsync(new("test", user.PhoneNumber, text, Guid.NewGuid().ToString()));
        Assert.Single(await db.CycleEvents.Where(x => x.Type == CycleEventTypes.FlowUpdate).ToListAsync());
        Assert.Null(user.PendingAction);
        Assert.Equal(new DateOnly(2026, 11, 9), (await db.Cycles.SingleAsync()).StartDate);
        foreach (var text in new[] { "registrar", "7" })
            await service.HandleIncomingMessageAsync(new("test", user.PhoneNumber, text, Guid.NewGuid().ToString()));
        var correction = new IncomingMessage("test", user.PhoneNumber, "hoje", "correct-once");
        var corrected = await service.HandleIncomingMessageAsync(correction);
        Assert.Contains("Corrigi", corrected);
        Assert.Equal(corrected, await service.HandleIncomingMessageAsync(correction));
        Assert.Equal(new DateOnly(2026, 11, 10), user.Preference!.LastPeriodStartDate);
        Assert.Single(await db.Cycles.ToListAsync());
        Assert.Equal(calls, model.Requests);
    }

    [Fact]
    public async Task Obvious_external_topics_do_not_invoke_the_model()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        var model = new ScriptedModel();
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", user.PhoneNumber, "qual a previsão do tempo?", "weather"));
        Assert.Contains("ciclo menstrual", reply);
        Assert.Equal(0, model.Requests);
    }

    [Fact]
    public async Task Essential_notification_menu_saves_time_and_timezone_without_AI()
    {
        await using var db = CreateDb();
        var user = await AddUser(db);
        db.AccountSubscriptions.Add(new AccountSubscription { PhoneNumber = user.PhoneNumber, PlanCode = "essencial", Status = SubscriptionStatuses.Active, CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30) });
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        var service = CreateService(db, model);
        foreach (var text in new[] { "registrar", "8", "3", "1", "1", "07:00", "6", "America/Manaus" })
            await service.HandleIncomingMessageAsync(new("test", user.PhoneNumber, text, Guid.NewGuid().ToString()));
        var settings = await db.NotificationPreferences.SingleAsync();
        Assert.True(settings.ContraceptiveReminderEnabled);
        Assert.Equal(new TimeOnly(7, 0), settings.ContraceptiveReminderTime);
        Assert.Equal("America/Manaus", settings.TimeZone);
        Assert.Equal("pill", user.Preference!.ContraceptiveType);
        Assert.Equal(0, model.Requests);
    }

    [PostgresFact]
    public async Task Batch_rolls_back_the_first_record_when_a_later_record_is_inconsistent()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")).Options;
        await using var db = new LumaDbContext(options);
        var phone = "+55169" + Random.Shared.Next(10000000, 99999999);
        var user = new LumaUser
        {
            PhoneNumber = phone,
            OnboardingStep = OnboardingSteps.Completed,
            ConsentAcceptedAt = DateTimeOffset.UtcNow,
            IsAdultConfirmed = true,
            Preference = new()
        };
        db.Users.Add(user); await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("record_actions", new
        {
            actions = new[] {
            new { tool = "record_period_start", arguments = new { date = "2026-11-10" } },
            new { tool = "record_period_end", arguments = new { date = "2026-11-09" } } }
        });
        var reply = await CreateService(db, model).HandleIncomingMessageAsync(new("test", phone, "começou hoje e terminou ontem", Guid.NewGuid().ToString()));
        Assert.Contains("Não concluí", reply);
        Assert.Empty(await db.Cycles.Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Empty(await db.CycleEvents.Where(x => x.UserId == user.Id).ToListAsync());
    }

    private static LumaDbContext CreateDb() => new(new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<LumaUser> AddUser(LumaDbContext db, string step = OnboardingSteps.Completed)
    {
        var user = new LumaUser
        {
            PhoneNumber = "+5516992000042",
            DisplayName = "Nay",
            IsAdultConfirmed = true,
            ConsentAcceptedAt = DateTimeOffset.UtcNow,
            OnboardingStep = step,
            Preference = new()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static ConversationService CreateService(LumaDbContext db, IAssistantModel model, UsageLedger? ledger = null, UsageContext? context = null) => new(
        db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Luma:RequireActiveSubscription"] = "false" }).Build(),
        null!, null!, new NullLumaToolAgent(), new PassthroughLumaResponseGenerator(), new FixedDate(), NullLogger<ConversationService>.Instance,
        assistant: new AssistantOrchestrator(model, Options.Create(new AssistantOptions { Enabled = true }), NullLogger<AssistantOrchestrator>.Instance),
        usage: ledger, usageContext: context);

    private sealed class FixedDate : IDateProvider { public DateTimeOffset UtcNow => new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero); }
    private sealed class ScriptedModel : IAssistantModel
    {
        private readonly Queue<JsonElement> _calls = new();
        public int Requests { get; private set; }
        public string LastInput { get; private set; } = "";
        public bool FailAfterCall { get; init; }
        public void Call(string name, object arguments) => _calls.Enqueue(JsonSerializer.SerializeToElement(new
        {
            type = "function_call",
            name,
            call_id = Guid.NewGuid().ToString(),
            arguments = JsonSerializer.Serialize(arguments)
        }));
        public Task<AssistantModelTurn> CompleteAsync(IReadOnlyList<object> input, IReadOnlyList<Tool> tools, bool requireTool, CancellationToken ct)
        {
            Requests++;
            LastInput = JsonSerializer.Serialize(input);
            if (_calls.TryDequeue(out var call)) return Task.FromResult(new AssistantModelTurn(null, [call]));
            if (FailAfterCall) throw new HttpRequestException("simulated");
            return Task.FromResult(new AssistantModelTurn("Combinado, deixei tudo organizado por aqui.", []));
        }
    }
}
