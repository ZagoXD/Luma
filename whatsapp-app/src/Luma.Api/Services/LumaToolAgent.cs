namespace Luma.Api.Services;

public interface ILumaToolAgent
{
    Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default);
}

public sealed record ConversationResult(string Body, string? MediaUrl = null);

public sealed record LumaToolAgentRequest(
    string UserMessage,
    DateOnly Today,
    ConversationContext Context,
    string? Knowledge,
    IReadOnlyList<string> AvailableTools);

public sealed class NullLumaToolAgent : ILumaToolAgent
{
    public Task<LumaToolCall?> DecideAsync(LumaToolAgentRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LumaToolCall?>(null);
    }
}

public sealed class LumaToolCall
{
    public string? ToolName { get; set; }
    public string? DisplayName { get; set; }
    public bool? ConsentAccepted { get; set; }
    public bool? IsAdultConfirmed { get; set; }
    public DateOnly? Date { get; set; }
    public int? AverageCycleLength { get; set; }
    public int? AveragePeriodLength { get; set; }
    public string? ContraceptiveType { get; set; }
    public string? FlowIntensity { get; set; }
    public string? Symptom { get; set; }
    public string? Intensity { get; set; }
    public string? Mood { get; set; }
    public string? Protected { get; set; }
    public bool? PeriodReminderEnabled { get; set; }
    public bool? ContraceptiveReminderEnabled { get; set; }
    public bool? SymptomCheckinEnabled { get; set; }
    public string? ReminderTime { get; set; }
    public string? TimeZone { get; set; }
    public int? GestationalWeeks { get; set; }
    public DateOnly? LastPeriodDate { get; set; }
    public DateOnly? EstimatedDueDate { get; set; }
    public int? BabyDevelopmentWeek { get; set; }
    public bool? GenerateBabyImage { get; set; }
    public string? CalendarMonth { get; set; }
    public double? Confidence { get; set; }
}
