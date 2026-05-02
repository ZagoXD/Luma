using Luma.Api.Services;

namespace Luma.Tests;

public sealed class BabyDevelopmentKnowledgeBaseTests
{
    [Fact]
    public void GetByWeek_ReturnsSafeDevelopmentInformation()
    {
        var info = BabyDevelopmentKnowledgeBase.GetByWeek(12);

        Assert.NotNull(info);
        Assert.Equal(12, info.Week);
        Assert.Contains("cm", info.SizeRange);
        Assert.Contains("estimativa", MessageText.Normalize(info.SafeNote));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(43)]
    public void GetByWeek_RejectsUnsupportedWeeks(int week)
    {
        Assert.Null(BabyDevelopmentKnowledgeBase.GetByWeek(week));
    }
}
