using System.Text.Json;
using Luma.Api.Services.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Luma.Tests;

public sealed class MultipleActionPolicyTests
{
    [Fact]
    public async Task Two_valid_record_operations_are_both_executed_and_confirmed_by_backend()
    {
        var model = new ActionsModel();
        var orchestrator = new AssistantOrchestrator(model, Options.Create(new AssistantOptions { Enabled = true }), NullLogger<AssistantOrchestrator>.Instance);
        var recorded = new List<string>();
        var reply = await orchestrator.RespondAsync(new("menstruei hoje e tive relação anteontem", "estado", new(2026, 11, 22), ""),
            (name, args, ct) => { recorded.Add(name); return Task.FromResult(new ToolOutcome(true, name == "record_period_start" ? "Menstruação: 22/11." : "Relação: 20/11.")); });
        Assert.Equal(2, recorded.Count);
        Assert.Contains("22/11", reply.Body);
        Assert.Contains("20/11", reply.Body);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public void Batch_rejects_unknown_tools_and_more_than_three_actions_before_execution()
    {
        Assert.NotNull(LumaToolCatalog.Validate("record_actions", JsonSerializer.SerializeToElement(new { actions = new[] { new { tool = "delete_all", arguments = new { } } } })));
        Assert.NotNull(LumaToolCatalog.Validate("record_actions", JsonSerializer.SerializeToElement(new { actions = Enumerable.Range(0, 4).Select(_ => new { tool = "record_period_start", arguments = new { date = "2026-11-22" } }) })));
    }

    private sealed class ActionsModel : IAssistantModel
    {
        public int Calls;
        public Task<AssistantModelTurn> CompleteAsync(IReadOnlyList<object> input, IReadOnlyList<Tool> tools, bool requireTool, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new AssistantModelTurn(null, [JsonSerializer.SerializeToElement(new{type="function_call",call_id="one",name="record_period_start",arguments="{\"date\":\"2026-11-22\"}"}),
                JsonSerializer.SerializeToElement(new{type="function_call",call_id="two",name="record_sexual_activity",arguments="{\"date\":\"2026-11-20\"}"})]));
        }
    }

    [Fact]
    public async Task Partial_interpretation_does_not_save_only_the_first_explicit_action()
    {
        var model = new PartialModel();
        var executed = 0;
        var service = new AssistantOrchestrator(model, Options.Create(new AssistantOptions { Enabled = true }), NullLogger<AssistantOrchestrator>.Instance);
        var reply = await service.RespondAsync(new("minha menstruação desceu hoje e tive relação anteontem", "estado", new(2026, 11, 22), ""),
            (_, _, _) => { executed++; return Task.FromResult(new ToolOutcome(true, "Registrei tudo.", true)); });
        Assert.Equal(0, executed);
        Assert.DoesNotContain("Registrei", reply.Body);
    }

    private sealed class PartialModel : IAssistantModel
    {
        public Task<AssistantModelTurn> CompleteAsync(IReadOnlyList<object> input, IReadOnlyList<Tool> tools, bool required, CancellationToken ct)
            => Task.FromResult(new AssistantModelTurn(null, [JsonSerializer.SerializeToElement(new {
                type = "function_call", call_id = Guid.NewGuid().ToString(), name = "record_period_start", arguments = "{\"date\":\"2026-11-22\"}" })]));
    }
}
