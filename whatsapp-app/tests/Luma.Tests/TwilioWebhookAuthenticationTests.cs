using System.Security.Cryptography;
using System.Text;
using Luma.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Luma.Tests;

public sealed class TwilioWebhookAuthenticationTests
{
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
