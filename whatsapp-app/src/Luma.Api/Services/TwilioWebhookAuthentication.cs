using Twilio.Security;

namespace Luma.Api.Services;

public static class TwilioWebhookAuthentication
{
    public static bool IsValid(string url, string token, string accountSid, string recipient,
        IFormCollection form, string signature, bool hasQuery)
    {
        if (hasQuery || string.IsNullOrWhiteSpace(signature) || form.Files.Count != 0
            || form.Any(field => field.Value.Count != 1)
            || !string.Equals(form["AccountSid"].ToString(), accountSid, StringComparison.Ordinal)
            || !string.Equals(form["To"].ToString(), recipient, StringComparison.Ordinal))
        {
            return false;
        }

        return new RequestValidator(token).Validate(url,
            form.ToDictionary(field => field.Key, field => field.Value.ToString(), StringComparer.Ordinal),
            signature);
    }
}
