using System.Security.Cryptography;
using System.Text;
using Luma.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Luma.Tests;

public sealed class TwilioWebhookAuthenticationTests
{
    [Theory]
    [InlineData("ACtest", "whatsapp:+16204008668", false, true)]
    [InlineData("ACwrong", "whatsapp:+16204008668", false, false)]
    [InlineData("ACtest", "whatsapp:+14155238886", false, false)]
    [InlineData("ACtest", "whatsapp:+16204008668", true, false)]
    public void Notification_callback_validates_the_signed_url_account_and_sender(string account, string sender, bool query, bool expected)
    {
        const string url = "https://api.ia-luma.com.br/webhooks/twilio/notifications/status/11111111-1111-1111-1111-111111111111";
        var fields = new Dictionary<string, string> {
            ["AccountSid"] = "ACtest", ["From"] = "whatsapp:+16204008668", ["To"] = "whatsapp:+5511999990001",
            ["MessageSid"] = "SMtest", ["MessageStatus"] = "delivered"
        };
        var form = new FormCollection(fields.ToDictionary(x => x.Key, x => new StringValues(x.Value)));
        var payload = url + string.Concat(fields.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + x.Value));
        var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes("token"), Encoding.UTF8.GetBytes(payload)));
        Assert.Equal(expected, TwilioWebhookAuthentication.IsValidNotificationStatus(url, "token", account, sender, form, signature, query));
        Assert.False(TwilioWebhookAuthentication.IsValidNotificationStatus(url + "/changed", "token", account, sender, form, signature, query));
    }

    [Theory]
    [InlineData("https://dev.ia-luma.com.br", "ACsandbox", "whatsapp:+14155238886", false, true)]
    [InlineData("https://ia-luma.com.br", "ACsandbox", "whatsapp:+14155238886", false, false)]
    [InlineData("https://dev.ia-luma.com.br", "ACother", "whatsapp:+14155238886", false, false)]
    [InlineData("https://dev.ia-luma.com.br", "ACsandbox", "whatsapp:+16204008668", false, false)]
    [InlineData("https://dev.ia-luma.com.br", "ACsandbox", "whatsapp:+14155238886", true, false)]
    public void IsValid_RequiresTheSignedUrlAndConfiguredChannel(
        string origin, string accountSid, string recipient, bool hasQuery, bool expected)
    {
        const string path = "/webhooks/twilio/whatsapp";
        const string token = "test-token";
        var fields = new Dictionary<string, string>
        {
            ["AccountSid"] = "ACsandbox",
            ["To"] = "whatsapp:+14155238886",
            ["Body"] = "Olá"
        };
        var form = new FormCollection(fields.ToDictionary(item => item.Key, item => new StringValues(item.Value)));
        var payload = "https://dev.ia-luma.com.br" + path + "AccountSidACsandboxBodyOláTowhatsapp:+14155238886";
        var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(payload)));

        Assert.Equal(expected, TwilioWebhookAuthentication.IsValid(
            origin + path, token, accountSid, recipient, form, signature, hasQuery));
    }
}
