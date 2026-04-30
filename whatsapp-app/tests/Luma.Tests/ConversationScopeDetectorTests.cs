using Luma.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Luma.Tests;

public sealed class ConversationScopeDetectorTests
{
    [Fact]
    public void DetectTwilio_AllowsIndividualWhatsAppNumber()
    {
        var form = Form(("From", "whatsapp:+5516992330309"));

        var result = new ConversationScopeDetector().DetectTwilio(form);

        Assert.False(result.IsGroup);
    }

    [Theory]
    [InlineData("whatsapp:120363123456789@g.us")]
    [InlineData("whatsapp:+5516992330309")]
    public void DetectTwilio_BlocksGroupSignals(string from)
    {
        var form = from.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase)
            ? Form(("From", from))
            : Form(("From", from), ("GroupSid", "GG123"));

        var result = new ConversationScopeDetector().DetectTwilio(form);

        Assert.True(result.IsGroup);
    }

    private static IFormCollection Form(params (string Key, string Value)[] values)
    {
        return new FormCollection(values.ToDictionary(
            item => item.Key,
            item => new StringValues(item.Value)));
    }
}
