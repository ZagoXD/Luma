using System.Text;
using System.Text.Json;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Luma.Api.Services;

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string WhatsAppFrom { get; set; } = string.Empty;
    public bool TypingIndicatorsEnabled { get; set; } = true;
    public string TemplatePeriodTomorrow { get; set; } = string.Empty;
    public string TemplatePeriodToday { get; set; } = string.Empty;
    public string TemplateContraceptiveDaily { get; set; } = string.Empty;
    public string TemplateSymptomCheckin { get; set; } = string.Empty;
    public string TemplateAccountVerification { get; set; } = string.Empty;
}

public sealed class NotificationOptions
{
    public bool WorkerEnabled { get; set; }
    public int WorkerIntervalSeconds { get; set; } = 60;
    public int DueWindowMinutes { get; set; } = 5;
}

public sealed record NotificationPreferenceUpdate(
    bool? PeriodReminderEnabled,
    bool? ContraceptiveReminderEnabled,
    bool? SymptomCheckinEnabled,
    string? ReminderTime,
    string? PeriodReminderTime,
    string? ContraceptiveReminderTime,
    string? SymptomCheckinTime,
    string? TimeZone);

public sealed record NotificationSendResult(bool Success, string? ProviderMessageId, string? ErrorMessage);

public sealed record TwilioVerifySendResult(bool Success, string? VerificationSid, string? Status, string? ErrorMessage);

public sealed record TwilioVerifyCheckResult(bool Approved, string? Status, string? ErrorMessage);

public interface ITwilioVerifyClient
{
    Task<TwilioVerifySendResult> SendVerificationAsync(string to, CancellationToken cancellationToken = default);
    Task<TwilioVerifyCheckResult> CheckVerificationAsync(string to, string code, CancellationToken cancellationToken = default);
}

public sealed class NotificationPreferenceService(LumaDbContext db)
{
    public async Task<NotificationPreference> UpsertAsync(Guid userId, NotificationPreferenceUpdate update)
    {
        var preference = await db.NotificationPreferences.FirstOrDefaultAsync(item => item.UserId == userId);
        if (preference is null)
        {
            preference = new NotificationPreference { UserId = userId };
            db.NotificationPreferences.Add(preference);
        }

        if (update.PeriodReminderEnabled is not null)
        {
            preference.PeriodReminderEnabled = update.PeriodReminderEnabled.Value;
        }

        if (update.ContraceptiveReminderEnabled is not null)
        {
            preference.ContraceptiveReminderEnabled = update.ContraceptiveReminderEnabled.Value;
        }

        if (update.SymptomCheckinEnabled is not null)
        {
            preference.SymptomCheckinEnabled = update.SymptomCheckinEnabled.Value;
        }

        if (!string.IsNullOrWhiteSpace(update.ReminderTime) && TryParseReminderTime(update.ReminderTime, out var reminderTime))
        {
            preference.ReminderTime = reminderTime;
            preference.PeriodReminderTime = reminderTime;
            preference.ContraceptiveReminderTime = reminderTime;
            preference.SymptomCheckinTime = reminderTime;
        }

        if (!string.IsNullOrWhiteSpace(update.PeriodReminderTime) && TryParseReminderTime(update.PeriodReminderTime, out var periodReminderTime))
        {
            preference.PeriodReminderTime = periodReminderTime;
        }

        if (!string.IsNullOrWhiteSpace(update.ContraceptiveReminderTime) && TryParseReminderTime(update.ContraceptiveReminderTime, out var contraceptiveReminderTime))
        {
            preference.ContraceptiveReminderTime = contraceptiveReminderTime;
        }

        if (!string.IsNullOrWhiteSpace(update.SymptomCheckinTime) && TryParseReminderTime(update.SymptomCheckinTime, out var symptomCheckinTime))
        {
            preference.SymptomCheckinTime = symptomCheckinTime;
        }

        if (!string.IsNullOrWhiteSpace(update.TimeZone))
        {
            preference.TimeZone = update.TimeZone;
        }

        preference.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return preference;
    }

    public static bool TryParseReminderTime(string value, out TimeOnly reminderTime)
    {
        var normalized = MessageText.Normalize(value)
            .Replace("às", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("as", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("h", ":", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (TimeOnly.TryParse(normalized, out reminderTime))
        {
            return true;
        }

        var digits = AccountInputNormalizer.OnlyDigits(normalized);
        if (digits.Length is 1 or 2 && int.TryParse(digits, out var hour) && hour is >= 0 and <= 23)
        {
            reminderTime = new TimeOnly(hour, 0);
            return true;
        }

        if (digits.Length == 4
            && int.TryParse(digits[..2], out var parsedHour)
            && int.TryParse(digits[2..], out var minute)
            && parsedHour is >= 0 and <= 23
            && minute is >= 0 and <= 59)
        {
            reminderTime = new TimeOnly(parsedHour, minute);
            return true;
        }

        reminderTime = default;
        return false;
    }
}

public interface IWhatsAppNotificationSender
{
    Task<NotificationSendResult> SendTemplateAsync(string to, string templateKey, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default);
}

public interface IWhatsAppTextSender
{
    Task<NotificationSendResult> SendTextAsync(string to, string body, CancellationToken cancellationToken = default);
}

public interface IWhatsAppMediaSender
{
    Task<NotificationSendResult> SendMediaAsync(string to, string body, string mediaUrl, CancellationToken cancellationToken = default);
}

public interface IWhatsAppTypingIndicatorSender
{
    Task<bool> TrySendAsync(string messageSid, CancellationToken cancellationToken = default);
}

public sealed class TwilioVerifyClient(HttpClient http, IConfiguration configuration, ILogger<TwilioVerifyClient> logger) : ITwilioVerifyClient
{
    private readonly string _accountSid = configuration.GetValue<string>("Twilio:AccountSid") ?? string.Empty;
    private readonly string _authToken = configuration.GetValue<string>("Twilio:AuthToken") ?? string.Empty;
    private readonly string _serviceSid = configuration.GetValue<string>("Twilio:VerifyServiceSid") ?? string.Empty;
    private readonly string _channel = configuration.GetValue<string>("Twilio:VerifyChannel") ?? "whatsapp";

    public async Task<TwilioVerifySendResult> SendVerificationAsync(string to, CancellationToken cancellationToken = default)
    {
        if (!HasConfiguration())
        {
            logger.LogInformation("Twilio Verify skipped because credentials or service SID are not configured.");
            return new TwilioVerifySendResult(false, null, null, "twilio_verify_not_configured");
        }

        var response = await PostFormAsync(
            $"https://verify.twilio.com/v2/Services/{_serviceSid}/Verifications",
            new Dictionary<string, string>
            {
                ["To"] = to,
                ["Channel"] = string.IsNullOrWhiteSpace(_channel) ? "whatsapp" : _channel
            },
            cancellationToken);

        if (!response.Success)
        {
            return new TwilioVerifySendResult(false, null, null, response.ErrorMessage);
        }

        using var document = JsonDocument.Parse(response.Body);
        var sid = document.RootElement.TryGetProperty("sid", out var sidProperty) ? sidProperty.GetString() : null;
        var status = document.RootElement.TryGetProperty("status", out var statusProperty) ? statusProperty.GetString() : null;
        return new TwilioVerifySendResult(true, sid, status, null);
    }

    public async Task<TwilioVerifyCheckResult> CheckVerificationAsync(string to, string code, CancellationToken cancellationToken = default)
    {
        if (!HasConfiguration())
        {
            logger.LogInformation("Twilio Verify check skipped because credentials or service SID are not configured.");
            return new TwilioVerifyCheckResult(false, null, "twilio_verify_not_configured");
        }

        var response = await PostFormAsync(
            $"https://verify.twilio.com/v2/Services/{_serviceSid}/VerificationCheck",
            new Dictionary<string, string>
            {
                ["To"] = to,
                ["Code"] = code
            },
            cancellationToken);

        if (!response.Success)
        {
            return new TwilioVerifyCheckResult(false, null, response.ErrorMessage);
        }

        using var document = JsonDocument.Parse(response.Body);
        var status = document.RootElement.TryGetProperty("status", out var statusProperty) ? statusProperty.GetString() : null;
        return new TwilioVerifyCheckResult(string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase), status, null);
    }

    private bool HasConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_accountSid)
            && !string.IsNullOrWhiteSpace(_authToken)
            && !string.IsNullOrWhiteSpace(_serviceSid);
    }

    private async Task<(bool Success, string Body, string? ErrorMessage)> PostFormAsync(
        string url,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        request.Content = new FormUrlEncodedContent(form);

        var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (true, body, null);
        }

        return (false, body, body.Length > 512 ? body[..512] : body);
    }
}

public sealed class TwilioWhatsAppNotificationSender(HttpClient http, IConfiguration configuration, ILogger<TwilioWhatsAppNotificationSender> logger) : IWhatsAppNotificationSender
{
    private readonly TwilioOptions _options = new()
    {
        AccountSid = configuration.GetValue<string>("Twilio:AccountSid") ?? string.Empty,
        AuthToken = configuration.GetValue<string>("Twilio:AuthToken") ?? string.Empty,
        WhatsAppFrom = configuration.GetValue<string>("Twilio:WhatsAppFrom") ?? string.Empty,
        TemplatePeriodTomorrow = configuration.GetValue<string>("Twilio:TemplatePeriodTomorrow") ?? string.Empty,
        TemplatePeriodToday = configuration.GetValue<string>("Twilio:TemplatePeriodToday") ?? string.Empty,
        TemplateContraceptiveDaily = configuration.GetValue<string>("Twilio:TemplateContraceptiveDaily") ?? string.Empty,
        TemplateSymptomCheckin = configuration.GetValue<string>("Twilio:TemplateSymptomCheckin") ?? string.Empty
    };

    public async Task<NotificationSendResult> SendTemplateAsync(string to, string templateKey, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        var templateSid = ResolveTemplate(templateKey);
        if (string.IsNullOrWhiteSpace(_options.AccountSid)
            || string.IsNullOrWhiteSpace(_options.AuthToken)
            || string.IsNullOrWhiteSpace(_options.WhatsAppFrom)
            || string.IsNullOrWhiteSpace(templateSid))
        {
            logger.LogInformation("Twilio template notification {TemplateKey} skipped because credentials/templates are not configured.", templateKey);
            return new NotificationSendResult(false, null, "twilio_template_not_configured");
        }

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = _options.WhatsAppFrom,
            ["To"] = to.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? to : $"whatsapp:{to}",
            ["ContentSid"] = templateSid,
            ["ContentVariables"] = JsonSerializer.Serialize(variables)
        });

        var response = await http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NotificationSendResult(false, null, content.Length > 512 ? content[..512] : content);
        }

        using var document = JsonDocument.Parse(content);
        var sid = document.RootElement.TryGetProperty("sid", out var sidProperty)
            ? sidProperty.GetString()
            : null;

        return new NotificationSendResult(true, sid, null);
    }

    private string ResolveTemplate(string templateKey)
    {
        return templateKey switch
        {
            NotificationTypes.PeriodExpectedTomorrow => _options.TemplatePeriodTomorrow,
            NotificationTypes.PeriodExpectedToday => _options.TemplatePeriodToday,
            NotificationTypes.ContraceptiveDaily => _options.TemplateContraceptiveDaily,
            NotificationTypes.SymptomCheckin => _options.TemplateSymptomCheckin,
            _ => string.Empty
        };
    }
}

public sealed class TwilioWhatsAppTextSender(HttpClient http, IConfiguration configuration, ILogger<TwilioWhatsAppTextSender> logger) : IWhatsAppTextSender
{
    private readonly TwilioOptions _options = new()
    {
        AccountSid = configuration.GetValue<string>("Twilio:AccountSid") ?? string.Empty,
        AuthToken = configuration.GetValue<string>("Twilio:AuthToken") ?? string.Empty,
        WhatsAppFrom = configuration.GetValue<string>("Twilio:WhatsAppFrom") ?? string.Empty,
        TemplateAccountVerification = configuration.GetValue<string>("Twilio:TemplateAccountVerification") ?? string.Empty
    };

    public async Task<NotificationSendResult> SendTextAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid)
            || string.IsNullOrWhiteSpace(_options.AuthToken)
            || string.IsNullOrWhiteSpace(_options.WhatsAppFrom))
        {
            logger.LogInformation("Twilio WhatsApp text skipped because credentials are not configured.");
            return new NotificationSendResult(false, null, "twilio_not_configured");
        }

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        var form = new Dictionary<string, string>
        {
            ["From"] = _options.WhatsAppFrom,
            ["To"] = to.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? to : $"whatsapp:{to}"
        };

        if (!string.IsNullOrWhiteSpace(_options.TemplateAccountVerification)
            && TryExtractVerificationCode(body, out var code))
        {
            form["ContentSid"] = _options.TemplateAccountVerification;
            form["ContentVariables"] = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["1"] = code
            });
        }
        else
        {
            form["Body"] = body;
        }

        request.Content = new FormUrlEncodedContent(form);

        var response = await http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NotificationSendResult(false, null, content.Length > 512 ? content[..512] : content);
        }

        using var document = JsonDocument.Parse(content);
        var sid = document.RootElement.TryGetProperty("sid", out var sidProperty)
            ? sidProperty.GetString()
            : null;

        return new NotificationSendResult(true, sid, null);
    }

    private static bool TryExtractVerificationCode(string body, out string code)
    {
        var digits = AccountInputNormalizer.OnlyDigits(body);
        if (digits.Length >= 6)
        {
            code = digits[..6];
            return true;
        }

        code = string.Empty;
        return false;
    }
}

public sealed class TwilioWhatsAppMediaSender(HttpClient http, IConfiguration configuration, ILogger<TwilioWhatsAppMediaSender> logger) : IWhatsAppMediaSender
{
    private readonly TwilioOptions _options = new()
    {
        AccountSid = configuration.GetValue<string>("Twilio:AccountSid") ?? string.Empty,
        AuthToken = configuration.GetValue<string>("Twilio:AuthToken") ?? string.Empty,
        WhatsAppFrom = configuration.GetValue<string>("Twilio:WhatsAppFrom") ?? string.Empty
    };

    public async Task<NotificationSendResult> SendMediaAsync(string to, string body, string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid)
            || string.IsNullOrWhiteSpace(_options.AuthToken)
            || string.IsNullOrWhiteSpace(_options.WhatsAppFrom))
        {
            logger.LogInformation("Twilio media message skipped because credentials are not configured.");
            return new NotificationSendResult(false, null, "twilio_media_not_configured");
        }

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = _options.WhatsAppFrom,
            ["To"] = to.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? to : $"whatsapp:{to}",
            ["Body"] = body,
            ["MediaUrl"] = mediaUrl
        });

        var response = await http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NotificationSendResult(false, null, content.Length > 512 ? content[..512] : content);
        }

        using var document = JsonDocument.Parse(content);
        var sid = document.RootElement.TryGetProperty("sid", out var sidProperty)
            ? sidProperty.GetString()
            : null;

        return new NotificationSendResult(true, sid, null);
    }
}

public sealed class TwilioWhatsAppTypingIndicatorSender(HttpClient http, IConfiguration configuration, ILogger<TwilioWhatsAppTypingIndicatorSender> logger) : IWhatsAppTypingIndicatorSender
{
    private readonly TwilioOptions _options = new()
    {
        AccountSid = configuration.GetValue<string>("Twilio:AccountSid") ?? string.Empty,
        AuthToken = configuration.GetValue<string>("Twilio:AuthToken") ?? string.Empty,
        TypingIndicatorsEnabled = configuration.GetValue("Twilio:TypingIndicatorsEnabled", true)
    };

    public async Task<bool> TrySendAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        if (!_options.TypingIndicatorsEnabled
            || string.IsNullOrWhiteSpace(messageSid)
            || !IsSupportedMessageSid(messageSid)
            || string.IsNullOrWhiteSpace(_options.AccountSid)
            || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://messaging.twilio.com/v2/Indicators/Typing.json");
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["messageId"] = messageSid,
                ["channel"] = "whatsapp"
            });

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogInformation("Twilio typing indicator skipped for {MessageSid}. Status {StatusCode}: {Response}", messageSid, (int)response.StatusCode, content.Length > 256 ? content[..256] : content);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Twilio typing indicator failed for {MessageSid}. Continuing without typing indicator.", messageSid);
            return false;
        }
    }

    private static bool IsSupportedMessageSid(string messageSid)
    {
        return messageSid.StartsWith("SM", StringComparison.OrdinalIgnoreCase)
            || messageSid.StartsWith("MM", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class NotificationProcessor(
    LumaDbContext db,
    RedisConnectionProvider redis,
    IWhatsAppNotificationSender sender,
    IDateProvider dateProvider,
    ILogger<NotificationProcessor> logger)
{
    public async Task<int> RunDueNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var now = dateProvider.UtcNow;
        var preferences = await db.NotificationPreferences
            .Include(preference => preference.User)
            .Where(preference => preference.PeriodReminderEnabled || preference.ContraceptiveReminderEnabled || preference.SymptomCheckinEnabled)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var preference in preferences)
        {
            if (preference.User is null)
            {
                continue;
            }

            processed += await ProcessUserAsync(preference, now, cancellationToken);
        }

        if (processed > 0)
        {
            logger.LogInformation("Processed {ProcessedNotifications} scheduled Luma notifications.", processed);
        }

        return processed;
    }

    private async Task<int> ProcessUserAsync(NotificationPreference preference, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = preference.User!;
        var phoneHash = PrivacyRuntime.LookupHash(user.PhoneNumber, "account.phone");
        var subscription = await db.AccountSubscriptions
            .Where(subscription => subscription.PhoneHash == phoneHash
                && subscription.PlanCode == "essencial"
                && subscription.CurrentPeriodEndsAt >= now
                && (subscription.Status == SubscriptionStatuses.Active || subscription.Status == SubscriptionStatuses.Canceled))
            .OrderByDescending(subscription => subscription.CurrentPeriodEndsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return 0;
        }

        if (await db.Pregnancies.AnyAsync(pregnancy => pregnancy.UserId == user.Id && pregnancy.Status == PregnancyStatus.Active, cancellationToken))
        {
            return 0;
        }

        var count = 0;
        var today = DateOnly.FromDateTime(now.Date);
        var userPreference = await db.UserPreferences.FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        if (preference.PeriodReminderEnabled
            && IsDueNow(preference, preference.PeriodReminderTime, now)
            && userPreference?.LastPeriodStartDate is not null)
        {
            var expected = userPreference.LastPeriodStartDate.Value.AddDays(userPreference.AverageCycleLength);
            if (expected == today.AddDays(1))
            {
                count += await TrySendAsync(preference, subscription.Id, NotificationTypes.PeriodExpectedTomorrow, today, now, cancellationToken);
            }
            else if (expected == today)
            {
                count += await TrySendAsync(preference, subscription.Id, NotificationTypes.PeriodExpectedToday, today, now, cancellationToken);
            }
        }

        if (preference.ContraceptiveReminderEnabled
            && IsDueNow(preference, preference.ContraceptiveReminderTime, now)
            && userPreference?.ContraceptiveType == "pill")
        {
            count += await TrySendAsync(preference, subscription.Id, NotificationTypes.ContraceptiveDaily, today, now, cancellationToken);
        }

        if (preference.SymptomCheckinEnabled && IsDueNow(preference, preference.SymptomCheckinTime, now))
        {
            count += await TrySendAsync(preference, subscription.Id, NotificationTypes.SymptomCheckin, today, now, cancellationToken);
        }

        return count;
    }

    private async Task<int> TrySendAsync(NotificationPreference preference, Guid subscriptionId, string type, DateOnly date, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = preference.User!;
        var lockKey = $"luma:notification-lock:{user.Id}:{type}:{date:yyyyMMdd}";
        var connection = await redis.GetConnectionAsync();
        if (connection is not null)
        {
            var acquired = await connection.GetDatabase().StringSetAsync(lockKey, "1", TimeSpan.FromHours(24), When.NotExists);
            if (!acquired)
            {
                return 0;
            }
        }

        if (await db.NotificationDeliveries.AnyAsync(delivery => delivery.UserId == user.Id && delivery.Type == type && delivery.ScheduledForDate == date, cancellationToken))
        {
            return 0;
        }

        var delivery = new NotificationDelivery
        {
            UserId = user.Id,
            AccountSubscriptionId = subscriptionId,
            Type = type,
            ScheduledForDate = date,
            ScheduledFor = now,
            Provider = "twilio"
        };
        db.NotificationDeliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken);

        var result = await sender.SendTemplateAsync(user.PhoneNumber, type, BuildVariables(preference, type), cancellationToken);
        delivery.Status = result.Success ? NotificationDeliveryStatuses.Sent : NotificationDeliveryStatuses.Failed;
        delivery.SentAt = result.Success ? now : null;
        delivery.ProviderMessageId = result.ProviderMessageId;
        delivery.ErrorMessage = result.ErrorMessage;
        delivery.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return 1;
    }

    private static Dictionary<string, string> BuildVariables(NotificationPreference preference, string type)
    {
        var user = preference.User!;
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "por aqui" : user.DisplayName;
        return type == NotificationTypes.ContraceptiveDaily
            ? new Dictionary<string, string> { ["1"] = name, ["2"] = preference.ContraceptiveReminderTime.ToString("HH:mm") }
            : new Dictionary<string, string> { ["1"] = name };
    }

    private static bool IsDueNow(NotificationPreference preference, TimeOnly reminderTime, DateTimeOffset utcNow)
    {
        const int dueWindowMinutes = 5;
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(preference.TimeZone);
        }
        catch
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var scheduledToday = new DateTimeOffset(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            reminderTime.Hour,
            reminderTime.Minute,
            0,
            localNow.Offset);

        return localNow >= scheduledToday && localNow < scheduledToday.AddMinutes(dueWindowMinutes);
    }
}

public sealed class NotificationWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<NotificationWorker> logger) : BackgroundService
{
    private readonly NotificationOptions _options = new()
    {
        WorkerEnabled = configuration.GetValue("Notifications:WorkerEnabled", false),
        WorkerIntervalSeconds = configuration.GetValue("Notifications:WorkerIntervalSeconds", 60)
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            logger.LogInformation("Notification worker disabled. Set Notifications__WorkerEnabled=true to enable it.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<NotificationProcessor>();
                await processor.RunDueNotificationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification worker failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.WorkerIntervalSeconds), stoppingToken);
        }
    }
}
