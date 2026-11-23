using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Luma.Api.Services.Assistant;

public static class AssistantToolSelection
{
    public static IReadOnlyList<Tool> ForContext(string context, IReadOnlyList<Tool> tools)
    {
        try
        {
            using var json = JsonDocument.Parse(context);
            if (!json.RootElement.TryGetProperty("stage", out var stage)) return tools;
            if (stage.GetString() == "completed")
                return tools.Where(x => x.Name is not ("get_onboarding_state" or "complete_onboarding_step")).ToArray();
            // Keep out-of-order registration available; omit unrelated personal queries during onboarding.
            return tools.Where(x => x.Name.StartsWith("record_", StringComparison.Ordinal)
                || x.Name is "complete_onboarding_step" or "get_onboarding_state" or "get_user_profile"
                or "get_plan_catalog" or "get_usage_summary" or "start_pregnancy_mode" or "search_luma_knowledge_base" or "medical_guardrail"
                or "ask_clarification" or "out_of_scope").ToArray();
        }
        catch (JsonException) { return tools; }
    }
}
