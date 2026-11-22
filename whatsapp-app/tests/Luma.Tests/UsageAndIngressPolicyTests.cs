using Luma.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Luma.Tests;

public sealed class UsageAndIngressPolicyTests
{
    [Theory]
    [InlineData("image/jpeg", "", IncomingContentKind.Unsupported)]
    [InlineData("video/mp4", "legenda", IncomingContentKind.UnsupportedWithCaption)]
    [InlineData("application/pdf", "", IncomingContentKind.Unsupported)]
    [InlineData("text/vcard", "", IncomingContentKind.Unsupported)]
    [InlineData("audio/ogg", "", IncomingContentKind.Audio)]
    public void Classifies_content_before_any_AI(string mime, string body, IncomingContentKind expected)
        => Assert.Equal(expected, IncomingContentPolicy.Classify(Form(("NumMedia", "1"), ("MediaContentType0", mime), ("Body", body), ("MediaUrl0", "https://api.twilio.com/media"))));

    [Fact]
    public void Location_and_contact_are_not_mistaken_for_empty_text()
    {
        Assert.Equal(IncomingContentKind.Unsupported, IncomingContentPolicy.Classify(Form(("NumMedia", "0"), ("Latitude", "-20"))));
        Assert.Equal(IncomingContentKind.Unsupported, IncomingContentPolicy.Classify(Form(("MessageType", "contacts"))));
        Assert.Equal(IncomingContentKind.Text, IncomingContentPolicy.Classify(Form(("Body", "menstruei hoje"))));
    }

    [Fact]
    public void Conversation_metadata_alone_is_not_proof_of_a_group()
        => Assert.False(new ConversationScopeDetector().DetectTwilio(Form(("From", "whatsapp:+5516992330309"), ("ConversationSid", "CH123"))).IsGroup);

    [Fact]
    public void Daily_limits_and_audio_policy_have_no_monthly_message_cap()
    {
        var options = new UsageOptions();
        Assert.Equal(10, options.DailyLimit("basico"));
        Assert.Equal(20, options.DailyLimit("essencial"));
        Assert.Equal(600, options.MonthlyAudioSeconds);
        Assert.Equal(60, options.MaxAudioSeconds);
    }

    [Theory]
    [InlineData("Qual é a previsão do tempo em São Paulo?", true)]
    [InlineData("Luma, qual é a previsão do tempo?", true)]
    [InlineData("Faça um código em Python para ordenar uma lista", true)]
    [InlineData("Qual é minha previsão de hoje?", false)]
    [InlineData("Meu namorado disse que a camisinha rasgou", false)]
    [InlineData("Estou triste e queria registrar meu humor", false)]
    public void Only_clearly_external_topics_are_rejected_locally(string message, bool blocked)
        => Assert.Equal(blocked, ConversationTopicPolicy.IsClearlyExternal(message));

    private static IFormCollection Form(params (string Key, string Value)[] fields)
        => new FormCollection(fields.ToDictionary(x => x.Key, x => new StringValues(x.Value)));
}
