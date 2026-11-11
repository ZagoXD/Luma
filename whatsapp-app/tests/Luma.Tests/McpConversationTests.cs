using System.Text.Json;
using Luma.Api.Services.Assistant;

namespace Luma.Tests;

public sealed class McpConversationTests
{
    [Fact]
    public async Task Mcp_discovers_tools_and_executes_only_the_bound_callback()
    {
        var called = false;
        await using var session = await LumaMcpSession.CreateAsync(async (name, arguments, ct) =>
        {
            called = true;
            await Task.CompletedTask;
            return new ToolOutcome(true, name + ":" + arguments.GetProperty("date").GetString());
        });
        var tools = await session.ListAsync(default);
        Assert.Contains(tools, tool => tool.Name == "record_period_start");
        Assert.Contains(tools, tool => tool.Name == "search_luma_knowledge_base");
        var outcome = await session.CallAsync("record_period_start", JsonSerializer.SerializeToElement(new { date = "2026-11-09" }), default);
        Assert.True(called);
        Assert.True(outcome.Success);
        Assert.Equal("record_period_start:2026-11-09", outcome.Message);
    }

    [Fact]
    public async Task Mcp_rejects_user_identity_and_unsupported_arguments()
    {
        var called = false;
        await using var session = await LumaMcpSession.CreateAsync((name, arguments, ct) =>
        {
            called = true;
            return Task.FromResult(new ToolOutcome(true, "unexpected"));
        });
        var outcome = await session.CallAsync("record_period_start",
            JsonSerializer.SerializeToElement(new { date = "2026-11-09", user_id = Guid.NewGuid() }), default);
        Assert.False(outcome.Success);
        Assert.False(called);
    }

    [Theory]
    [InlineData("record_period_start", "{}")]
    [InlineData("record_period_start", "{\"date\":\"ontem\"}")]
    [InlineData("update_user_preferences", "{\"average_cycle_length\":1}")]
    public void Tools_reject_invalid_arguments(string name, string json)
    {
        Assert.NotNull(LumaToolCatalog.Validate(name, JsonDocument.Parse(json).RootElement));
    }
}
