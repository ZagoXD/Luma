namespace Luma.Api.Services;

public static class OpenAiJsonSchemas
{
    private static readonly object StringOrNull = new { type = new[] { "string", "null" } };
    private static readonly object BooleanOrNull = new { type = new[] { "boolean", "null" } };
    private static readonly object NumberOrNull = new { type = new[] { "number", "null" } };
    private static readonly object IntegerOrNull = new { type = new[] { "integer", "null" } };

    public static object OnboardingExtraction => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "display_name",
            "is_adult_confirmed",
            "last_period_start_date",
            "last_period_days_ago",
            "last_period_unknown",
            "average_cycle_length",
            "average_period_length",
            "contraceptive_type"
        },
        properties = new Dictionary<string, object>
        {
            ["display_name"] = StringOrNull,
            ["is_adult_confirmed"] = BooleanOrNull,
            ["last_period_start_date"] = StringOrNull,
            ["last_period_days_ago"] = IntegerOrNull,
            ["last_period_unknown"] = new { type = "boolean" },
            ["average_cycle_length"] = IntegerOrNull,
            ["average_period_length"] = IntegerOrNull,
            ["contraceptive_type"] = EnumOrNull("pill", "injection", "hormonal_iud", "copper_iud", "implant", "condom", "none", "other", "prefer_not_say")
        }
    };

    public static object ConversationIntent => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "intent",
            "date",
            "gestational_weeks",
            "last_period_date",
            "estimated_due_date",
            "protected",
            "symptom",
            "intensity",
            "confidence"
        },
        properties = new Dictionary<string, object>
        {
            ["intent"] = EnumOrNull(
                ConversationIntents.PeriodStart,
                ConversationIntents.PeriodEnd,
                ConversationIntents.FlowUpdate,
                ConversationIntents.Symptom,
                ConversationIntents.Mood,
                ConversationIntents.SexualActivity,
                ConversationIntents.LastSexualActivityQuestion,
                ConversationIntents.PregnancyPositive,
                ConversationIntents.PregnancyBleeding,
                ConversationIntents.PregnancySymptom,
                ConversationIntents.PrenatalAppointment,
                ConversationIntents.Ultrasound,
                ConversationIntents.PregnancyWeeksQuestion,
                ConversationIntents.PregnancyDueDateQuestion,
                ConversationIntents.LumaIdentityQuestion,
                ConversationIntents.KnowledgeQuestion,
                ConversationIntents.OutOfScope),
            ["date"] = StringOrNull,
            ["gestational_weeks"] = IntegerOrNull,
            ["last_period_date"] = StringOrNull,
            ["estimated_due_date"] = StringOrNull,
            ["protected"] = EnumOrNull("yes", "no", "unknown", "prefer_not_say"),
            ["symptom"] = StringOrNull,
            ["intensity"] = EnumOrNull("light", "moderate", "strong"),
            ["confidence"] = NumberOrNull
        }
    };

    public static object ToolCall => new
    {
        type = "object",
        additionalProperties = false,
        required = new[]
        {
            "tool_name",
            "display_name",
            "consent_accepted",
            "is_adult_confirmed",
            "date",
            "average_cycle_length",
            "average_period_length",
            "contraceptive_type",
            "flow_intensity",
            "symptom",
            "intensity",
            "mood",
            "protected",
            "period_reminder_enabled",
            "contraceptive_reminder_enabled",
            "symptom_checkin_enabled",
            "reminder_time",
            "timezone",
            "gestational_weeks",
            "last_period_date",
            "estimated_due_date",
            "baby_development_week",
            "generate_baby_image",
            "calendar_month",
            "confidence"
        },
        properties = new Dictionary<string, object>
        {
            ["tool_name"] = EnumOrNull(
                "complete_onboarding_step",
                "save_pending_intent",
                "record_period_start",
                "record_period_end",
                "record_flow_update",
                "record_symptom",
                "record_mood",
                "record_sexual_activity",
                "start_pregnancy_mode",
                "record_pregnancy_bleeding",
                "record_pregnancy_symptom",
                "record_prenatal_appointment",
                "record_ultrasound",
                "get_baby_development",
                "generate_baby_size_image",
                "get_cycle_calendar",
                "calculate_next_period",
                "calculate_delay",
                "get_last_period",
                "get_last_symptom",
                "get_last_sexual_activity",
                "get_notification_preferences",
                "update_notification_preferences",
                "disable_notification_preferences",
                "search_luma_knowledge_base",
                "out_of_scope",
                "medical_guardrail"),
            ["display_name"] = StringOrNull,
            ["consent_accepted"] = BooleanOrNull,
            ["is_adult_confirmed"] = BooleanOrNull,
            ["date"] = StringOrNull,
            ["average_cycle_length"] = IntegerOrNull,
            ["average_period_length"] = IntegerOrNull,
            ["contraceptive_type"] = EnumOrNull("pill", "injection", "hormonal_iud", "copper_iud", "implant", "condom", "none", "other", "prefer_not_say"),
            ["flow_intensity"] = EnumOrNull("light", "medium", "intense", "unknown"),
            ["symptom"] = StringOrNull,
            ["intensity"] = EnumOrNull("light", "moderate", "strong"),
            ["mood"] = StringOrNull,
            ["protected"] = EnumOrNull("yes", "no", "unknown", "prefer_not_say"),
            ["period_reminder_enabled"] = BooleanOrNull,
            ["contraceptive_reminder_enabled"] = BooleanOrNull,
            ["symptom_checkin_enabled"] = BooleanOrNull,
            ["reminder_time"] = StringOrNull,
            ["timezone"] = StringOrNull,
            ["gestational_weeks"] = IntegerOrNull,
            ["last_period_date"] = StringOrNull,
            ["estimated_due_date"] = StringOrNull,
            ["baby_development_week"] = IntegerOrNull,
            ["generate_baby_image"] = BooleanOrNull,
            ["calendar_month"] = StringOrNull,
            ["confidence"] = NumberOrNull
        }
    };

    public static object LumaReply => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "reply" },
        properties = new Dictionary<string, object>
        {
            ["reply"] = new { type = "string" }
        }
    };

    private static object EnumOrNull(params string[] values)
    {
        return new
        {
            type = new[] { "string", "null" },
            @enum = values.Concat([null!]).ToArray()
        };
    }
}
