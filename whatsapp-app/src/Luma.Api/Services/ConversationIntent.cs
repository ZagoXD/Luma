namespace Luma.Api.Services;

public sealed class ConversationIntent
{
    public string? Intent { get; set; }
    public DateOnly? Date { get; set; }
    public int? GestationalWeeks { get; set; }
    public DateOnly? LastPeriodDate { get; set; }
    public DateOnly? EstimatedDueDate { get; set; }
    public string? Protected { get; set; }
    public string? Symptom { get; set; }
    public string? Intensity { get; set; }
    public double? Confidence { get; set; }
}

public static class ConversationIntents
{
    public const string PeriodStart = "period_start";
    public const string PeriodEnd = "period_end";
    public const string FlowUpdate = "flow_update";
    public const string Symptom = "symptom";
    public const string Mood = "mood";
    public const string SexualActivity = "sexual_activity";
    public const string LastSexualActivityQuestion = "last_sexual_activity_question";
    public const string PregnancyPositive = "pregnancy_positive";
    public const string PregnancyBleeding = "pregnancy_bleeding";
    public const string PregnancySymptom = "pregnancy_symptom";
    public const string PrenatalAppointment = "prenatal_appointment";
    public const string Ultrasound = "ultrasound";
    public const string PregnancyWeeksQuestion = "pregnancy_weeks_question";
    public const string PregnancyDueDateQuestion = "pregnancy_due_date_question";
    public const string LumaIdentityQuestion = "luma_identity_question";
    public const string KnowledgeQuestion = "knowledge_question";
    public const string OutOfScope = "out_of_scope";
}
