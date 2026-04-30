using Luma.Api.Services;

namespace Luma.Tests;

public sealed class NotificationPreferenceServiceTests
{
    [Theory]
    [InlineData("8", 8, 0)]
    [InlineData("08:30", 8, 30)]
    [InlineData("20h", 20, 0)]
    [InlineData("às 21h15", 21, 15)]
    public void TryParseReminderTime_AcceptsNaturalTimes(string text, int hour, int minute)
    {
        var parsed = NotificationPreferenceService.TryParseReminderTime(text, out var reminderTime);

        Assert.True(parsed);
        Assert.Equal(new TimeOnly(hour, minute), reminderTime);
    }

    [Theory]
    [InlineData("amanhã")]
    [InlineData("25:90")]
    public void TryParseReminderTime_RejectsInvalidTimes(string text)
    {
        var parsed = NotificationPreferenceService.TryParseReminderTime(text, out _);

        Assert.False(parsed);
    }
}
