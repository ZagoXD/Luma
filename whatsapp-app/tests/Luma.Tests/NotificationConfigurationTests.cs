using Luma.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Luma.Tests;

public sealed class NotificationConfigurationTests
{
    [Fact]
    public void Disabled_worker_needs_no_provider_credentials() => NotificationConfiguration.Validate(new ConfigurationBuilder().Build());

    [Theory]
    [InlineData("Twilio:TemplateContraceptiveDaily", "")]
    [InlineData("Twilio:NotificationStatusCallbackBaseUrl", "http://localhost/status")]
    [InlineData("Notifications:WorkerIntervalSeconds", "600")]
    [InlineData("Twilio:AuthToken", "")]
    public void Enabled_worker_rejects_incomplete_or_unsafe_configuration(string key, string value)
    {
        var fields = Valid(); fields[key] = value;
        Assert.Throws<InvalidOperationException>(() => NotificationConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(fields).Build()));
    }

    [Fact]
    public void Complete_configuration_can_enable_the_worker() => NotificationConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(Valid()).Build());

    private static Dictionary<string, string?> Valid() => new() {
        ["Notifications:WorkerEnabled"] = "true", ["Twilio:AccountSid"] = "ACtest", ["Twilio:AuthToken"] = "test",
        ["Twilio:WhatsAppFrom"] = "whatsapp:+16204008668",
        ["Twilio:NotificationStatusCallbackBaseUrl"] = "https://api.ia-luma.com.br/webhooks/twilio/notifications/status",
        ["Twilio:TemplatePeriodTomorrow"] = "HX" + new string('a', 32), ["Twilio:TemplatePeriodToday"] = "HX" + new string('a', 32),
        ["Twilio:TemplateContraceptiveDaily"] = "HX" + new string('a', 32), ["Twilio:TemplateSymptomCheckin"] = "HX" + new string('a', 32)
    };
}
