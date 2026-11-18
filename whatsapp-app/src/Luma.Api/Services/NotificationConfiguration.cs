using System.Text.RegularExpressions;

namespace Luma.Api.Services;

public static class NotificationConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Notifications:WorkerEnabled")) return;
        var required = new[] { "AccountSid", "AuthToken", "WhatsAppFrom" };
        if (required.Any(key => string.IsNullOrWhiteSpace(configuration["Twilio:" + key])))
            throw new InvalidOperationException("Notification worker requires configured Twilio credentials and sender.");
        foreach (var name in new[] { "PeriodTomorrow", "PeriodToday", "ContraceptiveDaily", "SymptomCheckin" })
            if (!Regex.IsMatch(configuration["Twilio:Template" + name] ?? "", "^HX[0-9a-fA-F]{32}$"))
                throw new InvalidOperationException("Notification worker requires all four approved template SIDs.");
        if (!Uri.TryCreate(configuration["Twilio:NotificationStatusCallbackBaseUrl"], UriKind.Absolute, out var callback)
            || callback.Scheme != "https" || callback.Query != "" || callback.Fragment != "" || callback.UserInfo != ""
            || callback.AbsolutePath.TrimEnd('/') != "/webhooks/twilio/notifications/status")
            throw new InvalidOperationException("Notification worker requires a public HTTPS status callback base URL.");
        var options = configuration.GetSection("Notifications").Get<NotificationOptions>() ?? new();
        if (options.DueWindowMinutes is < 1 or > 30 || options.WorkerIntervalSeconds < 5
            || options.WorkerIntervalSeconds > Math.Min(60, options.DueWindowMinutes * 30)
            || options.MaxSendAttempts is < 1 or > 5 || options.RetryDelaySeconds is < 30 or > 300)
            throw new InvalidOperationException("Invalid notification polling or retry configuration.");
    }
}
