namespace Luma.Api.Services;

public static class PhoneVerificationPolicy
{
    public const string DevelopmentCode = "123456";

    public static bool UseDevelopmentClient(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Twilio:VerifyDevelopmentMode")) return false;
        if (!string.Equals(configuration["Deployment:Environment"], "dev", StringComparison.Ordinal))
            throw new InvalidOperationException("Simulated phone verification is only permitted in the dev deployment.");
        return true;
    }

    public static string ChannelLabel(string channel) => channel == "sms" ? "SMS" : "WhatsApp";
}

public sealed class DevelopmentVerifyClient : ITwilioVerifyClient
{
    public string Channel => "development";

    public Task<TwilioVerifySendResult> SendVerificationAsync(string to, CancellationToken cancellationToken = default)
        => Task.FromResult(new TwilioVerifySendResult(true, null, "pending", null));

    public Task<TwilioVerifyCheckResult> CheckVerificationAsync(string to, string code, CancellationToken cancellationToken = default)
        => Task.FromResult(new TwilioVerifyCheckResult(code == PhoneVerificationPolicy.DevelopmentCode,
            code == PhoneVerificationPolicy.DevelopmentCode ? "approved" : "pending", null));
}
