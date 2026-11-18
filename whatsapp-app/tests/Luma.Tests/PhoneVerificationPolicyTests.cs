using Luma.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Luma.Tests;

public sealed class PhoneVerificationPolicyTests
{
    [Theory]
    [InlineData("dev", true, true)]
    [InlineData("production", false, false)]
    [InlineData(null, false, false)]
    public void Development_verification_requires_explicit_configuration(string? environment, bool enabled, bool expected)
    {
        Assert.Equal(expected, PhoneVerificationPolicy.UseDevelopmentClient(Config(environment, enabled)));
    }

    [Theory]
    [InlineData("production")]
    [InlineData(null)]
    public void Development_verification_is_never_enabled_in_production(string? environment)
    {
        Assert.Throws<InvalidOperationException>(() => PhoneVerificationPolicy.UseDevelopmentClient(Config(environment, true)));
    }

    [Theory]
    [InlineData("sms", "SMS")]
    [InlineData("whatsapp", "WhatsApp")]
    public void Human_readable_channel_matches_the_provider(string channel, string expected)
    {
        Assert.Equal(expected, PhoneVerificationPolicy.ChannelLabel(channel));
    }

    [Fact]
    public async Task Development_client_only_accepts_its_documented_test_code()
    {
        var client = new DevelopmentVerifyClient();
        Assert.True((await client.SendVerificationAsync("+5511999990001")).Success);
        Assert.False((await client.CheckVerificationAsync("+5511999990001", "654321")).Approved);
        Assert.True((await client.CheckVerificationAsync("+5511999990001", PhoneVerificationPolicy.DevelopmentCode)).Approved);
    }

    private static IConfiguration Config(string? environment, bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Deployment:Environment"] = environment,
            ["Twilio:VerifyDevelopmentMode"] = enabled.ToString()
        }).Build();
}
