namespace Luma.Api.Services;

public sealed class OnboardingExtraction
{
    public string? DisplayName { get; set; }
    public bool? IsAdultConfirmed { get; set; }
    public DateOnly? LastPeriodStartDate { get; set; }
    public int? LastPeriodDaysAgo { get; set; }
    public bool LastPeriodUnknown { get; set; }
    public int? AverageCycleLength { get; set; }
    public int? AveragePeriodLength { get; set; }
    public string? ContraceptiveType { get; set; }

    public bool HasAnyValue()
    {
        return DisplayName is not null
            || IsAdultConfirmed is not null
            || LastPeriodStartDate is not null
            || LastPeriodDaysAgo is not null
            || LastPeriodUnknown
            || AverageCycleLength is not null
            || AveragePeriodLength is not null
            || ContraceptiveType is not null;
    }
}
