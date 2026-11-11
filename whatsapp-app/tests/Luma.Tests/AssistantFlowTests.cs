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
        var user = new LumaUser { PhoneNumber = phone, OnboardingStep = OnboardingSteps.Completed,
            IsAdultConfirmed = true, ConsentAcceptedAt = DateTimeOffset.UtcNow, Preference = new() };
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
        Assert.Equal(2, model.Requests);
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
        db.AccountSubscriptions.Add(new AccountSubscription { PhoneNumber = user.PhoneNumber,
            PlanCode = "essencial", Status = SubscriptionStatuses.Active,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30) });
        await db.SaveChangesAsync();
        var model = new ScriptedModel();
        model.Call("update_notification_preferences", new { contraceptive_type = "pill",
            period_reminder_enabled = true, contraceptive_reminder_enabled = true, symptom_checkin_enabled = true,
            period_reminder_time = "09:30", contraceptive_reminder_time = "07:00", symptom_checkin_time = "20:00" });
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

    private static LumaDbContext CreateDb() => new(new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<LumaUser> AddUser(LumaDbContext db, string step = OnboardingSteps.Completed)
    {
        var user = new LumaUser { PhoneNumber = "+5516992000042", DisplayName = "Nay", IsAdultConfirmed = true,
            ConsentAcceptedAt = DateTimeOffset.UtcNow, OnboardingStep = step, Preference = new() };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static ConversationService CreateService(LumaDbContext db, IAssistantModel model) => new(
        db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Luma:RequireActiveSubscription"] = "false" }).Build(),
        null!, null!, new NullLumaToolAgent(), new PassthroughLumaResponseGenerator(), new FixedDate(), NullLogger<ConversationService>.Instance,
        assistant: new AssistantOrchestrator(model, Options.Create(new AssistantOptions { Enabled = true }), NullLogger<AssistantOrchestrator>.Instance));

    private sealed class FixedDate : IDateProvider { public DateTimeOffset UtcNow => new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero); }
    private sealed class ScriptedModel : IAssistantModel
    {
        private readonly Queue<JsonElement> _calls = new();
        public int Requests { get; private set; }
        public bool FailAfterCall { get; init; }
        public void Call(string name, object arguments) => _calls.Enqueue(JsonSerializer.SerializeToElement(new
        {
            type = "function_call", name, call_id = Guid.NewGuid().ToString(), arguments = JsonSerializer.Serialize(arguments)
        }));
        public Task<AssistantModelTurn> CompleteAsync(IReadOnlyList<object> input, IReadOnlyList<Tool> tools, bool requireTool, CancellationToken ct)
        {
            Requests++;
            if (_calls.TryDequeue(out var call)) return Task.FromResult(new AssistantModelTurn(null, [call]));
            if (FailAfterCall) throw new HttpRequestException("simulated");
            return Task.FromResult(new AssistantModelTurn("Combinado, deixei tudo organizado por aqui.", []));
        }
    }
}
