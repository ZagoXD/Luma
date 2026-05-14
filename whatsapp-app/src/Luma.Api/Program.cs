using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using LumaSubscriptionStatuses = Luma.Api.Models.SubscriptionStatuses;
using StripeSubscription = Stripe.Subscription;

var builder = WebApplication.CreateBuilder(args);
PrivacyRuntime.Configure(builder.Configuration.GetSection("Privacy").Get<PrivacyOptions>() ?? new PrivacyOptions());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddDbContext<LumaDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=luma;Username=luma;Password=luma_dev_password";

    options.UseNpgsql(connectionString);
});
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<R2Options>(builder.Configuration.GetSection("R2"));
builder.Services.Configure<ElevenLabsOptions>(builder.Configuration.GetSection("ElevenLabs"));
builder.Services.Configure<StripeBillingOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection("Resend"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<EmailTemplateOptions>(builder.Configuration.GetSection("Email:Templates"));
builder.Services.PostConfigure<EmailOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.WebBaseUrl))
    {
        options.WebBaseUrl = builder.Configuration.GetValue<string>("Luma:WebBaseUrl")
            ?? builder.Configuration.GetValue<string>("LUMA_WEB_BASE_URL")
            ?? "http://localhost:3000";
    }
});
builder.Services.AddHttpClient<OpenAiResponsesClient>();
builder.Services.AddHttpClient<IEmailService, ResendEmailService>();
builder.Services.AddHttpClient<IBabyImageService, BabyImageService>();
builder.Services.AddHttpClient<ISpeechToTextService, ElevenLabsSpeechToTextService>();
builder.Services.AddHttpClient<ITwilioMediaDownloader, TwilioMediaDownloader>();
builder.Services.AddScoped<IOnboardingDataExtractor, OpenAiOnboardingDataExtractor>();
builder.Services.AddScoped<IConversationIntentExtractor, OpenAiConversationIntentExtractor>();
builder.Services.AddScoped<ILumaToolAgent, OpenAiLumaToolAgent>();
builder.Services.AddScoped<ILumaResponseGenerator, OpenAiLumaResponseGenerator>();
builder.Services.AddScoped<IWhatsAppAudioTranscriptionService, WhatsAppAudioTranscriptionService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RedisConnectionProvider>();
builder.Services.AddSingleton<MessageIngressGuard>();
builder.Services.AddSingleton<ConversationScopeDetector>();
builder.Services.AddHttpClient<IWhatsAppNotificationSender, TwilioWhatsAppNotificationSender>();
builder.Services.AddHttpClient<IWhatsAppTextSender, TwilioWhatsAppTextSender>();
builder.Services.AddHttpClient<IWhatsAppMediaSender, TwilioWhatsAppMediaSender>();
builder.Services.AddHttpClient<IWhatsAppTypingIndicatorSender, TwilioWhatsAppTypingIndicatorSender>();
builder.Services.AddHttpClient<ITwilioVerifyClient, TwilioVerifyClient>();
builder.Services.AddSingleton<BabyImageJobQueue>();
builder.Services.AddSingleton<IBabyImageJobQueue>(provider => provider.GetRequiredService<BabyImageJobQueue>());
builder.Services.AddScoped<AccountPhoneVerificationService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<SupportRequestService>();
builder.Services.AddScoped<NotificationPreferenceService>();
builder.Services.AddScoped<NotificationProcessor>();
builder.Services.AddScoped<CycleCalendarService>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<BabyImageWorker>();

builder.Services.AddSingleton<IDateProvider, SystemDateProvider>();
builder.Services.AddScoped<ConversationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureRuntimeSchemaAsync(db);
}

app.MapGet("/health", async (LumaDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { status = "ok", database = "connected", service = "luma-api" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/account/register", async (
    AccountRegisterRequest request,
    LumaDbContext db,
    AccountPhoneVerificationService phoneVerification,
    IEmailService emailService,
    ILogger<Program> logger) =>
{
    if (AccountConsentPolicy.ValidateDataConsent(request.DataConsentAccepted) is { } consentError)
    {
        return Results.BadRequest(new { message = consentError });
    }

    var normalized = AccountInputNormalizer.NormalizeRegistration(
        request.Email,
        request.Cpf,
        request.PhoneNumber,
        request.FullName,
        request.Password);

    if (normalized.Error is not null)
    {
        return Results.BadRequest(new { message = normalized.Error });
    }

    var emailHash = PrivacyRuntime.LookupHash(normalized.Email, "account.email");
    var cpfHash = PrivacyRuntime.LookupHash(normalized.Cpf, "account.cpf");
    var phoneHash = PrivacyRuntime.LookupHash(normalized.PhoneNumber, "account.phone");
    var exists = await db.AccountUsers.AnyAsync(user =>
        user.EmailHash == emailHash || user.CpfHash == cpfHash || user.PhoneHash == phoneHash);

    if (exists)
    {
        return Results.Conflict(new { message = "Já existe uma conta com esse e-mail, CPF ou celular." });
    }

    var account = new AccountUser
    {
        Email = normalized.Email,
        Cpf = normalized.Cpf,
        FullName = normalized.FullName,
        PhoneNumber = normalized.PhoneNumber,
        PasswordHash = AccountSecurity.HashPassword(request.Password)
    };

    db.AccountUsers.Add(account);
    await db.SaveChangesAsync();
    var verification = await phoneVerification.SendCurrentPhoneCodeAsync(account);
    var welcomeEmail = await emailService.SendWelcomeEmailAsync(account.Email, account.FullName);
    if (!welcomeEmail.Success)
    {
        logger.LogWarning("welcome_email_failed for account {AccountId}: {Error}", account.Id, welcomeEmail.ErrorMessage);
    }

    return Results.Ok(BuildAccountAuthResponse(account, builder.Configuration, verification.Message));
})
.WithName("RegisterAccount")
.WithOpenApi();

app.MapPost("/auth/forgot-password", async (
    ForgotPasswordRequest request,
    HttpContext http,
    PasswordResetService passwordReset) =>
{
    var result = await passwordReset.RequestResetAsync(
        request.Email,
        http.Connection.RemoteIpAddress?.ToString(),
        http.Request.Headers.UserAgent.ToString(),
        http.RequestAborted);

    return Results.Ok(new { message = result.Message });
})
.WithName("ForgotPassword")
.WithOpenApi();

app.MapPost("/auth/reset-password", async (
    ResetPasswordRequest request,
    PasswordResetService passwordReset) =>
{
    var result = await passwordReset.ResetPasswordAsync(request.Token, request.NewPassword);
    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
})
.WithName("ResetPassword")
.WithOpenApi();

app.MapPost("/support/requests", async (
    HttpRequest request,
    LumaDbContext db,
    SupportRequestService supportRequests,
    IOptions<EmailOptions> emailOptions) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "Envie a solicitação usando multipart/form-data." });
    }

    var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
    var supportEmailOptions = emailOptions.Value;
    var maxAttachments = Math.Max(0, supportEmailOptions.MaxSupportAttachments);
    var maxAttachmentBytes = Math.Max(1, supportEmailOptions.MaxSupportAttachmentBytes);
    if (form.Files.Count > maxAttachments)
    {
        return Results.BadRequest(new { message = $"Envie no máximo {maxAttachments} anexos." });
    }

    var attachments = new List<SupportAttachmentInput>();
    foreach (var file in form.Files)
    {
        if (file.Length > maxAttachmentBytes)
        {
            return Results.BadRequest(new { message = $"Cada anexo deve ter no máximo {FormatSupportAttachmentSize(maxAttachmentBytes)}." });
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, request.HttpContext.RequestAborted);
        attachments.Add(new SupportAttachmentInput(file.FileName, file.ContentType, memory.ToArray()));
    }

    var result = await supportRequests.CreateAsync(
        account,
        form["subject"].ToString(),
        form["description"].ToString(),
        attachments,
        request.HttpContext.RequestAborted);

    return result.Success
        ? Results.Ok(new { message = result.Message, id = result.Request?.Id })
        : Results.BadRequest(new { message = result.Message });
})
.DisableAntiforgery()
.WithName("CreateSupportRequest")
.WithOpenApi();

static string FormatSupportAttachmentSize(long bytes)
{
    var megabytes = bytes / 1024d / 1024d;
    return megabytes >= 1 ? $"{megabytes:0.#} MB" : $"{bytes} bytes";
}

app.MapPost("/account/login", async (AccountLoginRequest request, LumaDbContext db) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var emailHash = PrivacyRuntime.LookupHash(email, "account.email");
    var account = await db.AccountUsers.FirstOrDefaultAsync(user => user.EmailHash == emailHash);
    if (account is null || !AccountSecurity.VerifyPassword(request.Password, account.PasswordHash))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(BuildAccountAuthResponse(account, builder.Configuration));
})
.WithName("LoginAccount")
.WithOpenApi();

app.MapGet("/account/me", async (HttpRequest request, LumaDbContext db) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    var userPhoneHash = PrivacyRuntime.LookupHash(account.PhoneNumber, "user.phone");
    var lumaUser = await db.Users
        .AsNoTracking()
        .Include(user => user.Preference)
        .FirstOrDefaultAsync(user => user.PhoneHash == userPhoneHash);

    return Results.Ok(new
    {
        user = BuildAccountUserResponse(account),
        subscription = subscription is null ? null : BuildSubscriptionResponse(subscription),
        menstrual = lumaUser is null
            ? null
            : new
            {
                displayName = lumaUser.DisplayName,
                onboardingStep = lumaUser.OnboardingStep,
                isAdultConfirmed = lumaUser.IsAdultConfirmed,
                lastPeriodStartDate = lumaUser.Preference?.LastPeriodStartDate,
                averageCycleLength = lumaUser.Preference?.AverageCycleLength,
                averagePeriodLength = lumaUser.Preference?.AveragePeriodLength,
                contraceptiveType = lumaUser.Preference?.ContraceptiveType
            }
    });
})
.WithName("GetAccountProfile")
.WithOpenApi();

app.MapPost("/account/phone-verification/send", async (
    HttpRequest request,
    LumaDbContext db,
    AccountPhoneVerificationService phoneVerification) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var result = await phoneVerification.SendCurrentPhoneCodeAsync(account);
    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
})
.WithName("SendAccountPhoneVerification")
.WithOpenApi();

app.MapPost("/account/phone-verification/confirm", async (
    HttpRequest request,
    PhoneVerificationConfirmRequest confirm,
    LumaDbContext db,
    AccountPhoneVerificationService phoneVerification) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var result = await phoneVerification.ConfirmCurrentPhoneCodeAsync(account, confirm.Code);
    return result.Success
        ? Results.Ok(new { message = result.Message, user = BuildAccountUserResponse(account) })
        : Results.BadRequest(new { message = result.Message });
})
.WithName("ConfirmAccountPhoneVerification")
.WithOpenApi();

app.MapPost("/account/phone-change/request", async (
    HttpRequest request,
    PhoneChangeRequest phoneChange,
    LumaDbContext db,
    AccountPhoneVerificationService phoneVerification) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var result = await phoneVerification.SendPhoneChangeCodeAsync(account, phoneChange.PhoneNumber);
    return result.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { message = result.Message });
})
.WithName("RequestAccountPhoneChange")
.WithOpenApi();

app.MapPost("/account/phone-change/confirm", async (
    HttpRequest request,
    PhoneChangeConfirmRequest phoneChange,
    LumaDbContext db,
    AccountPhoneVerificationService phoneVerification) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var result = await phoneVerification.ConfirmPhoneChangeCodeAsync(account, phoneChange.PhoneNumber, phoneChange.Code);
    return result.Success
        ? Results.Ok(new { message = result.Message, user = BuildAccountUserResponse(account) })
        : Results.BadRequest(new { message = result.Message });
})
.WithName("ConfirmAccountPhoneChange")
.WithOpenApi();

app.MapGet("/account/calendar", async (
    HttpRequest request,
    string? month,
    LumaDbContext db,
    CycleCalendarService calendars) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var targetMonth = YearMonth.TryParse(month, out var parsed)
        ? parsed
        : new YearMonth(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month);

    var lumaUser = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(user => user.PhoneHash == PrivacyRuntime.LookupHash(account.PhoneNumber, "user.phone"));

    if (lumaUser is null)
    {
        return Results.NotFound(new { message = "A Luma ainda não recebeu dados pelo WhatsApp para este celular." });
    }

    var calendar = await calendars.BuildMonthAsync(lumaUser.Id, targetMonth);
    return calendar is null
        ? Results.NotFound(new { message = "Calendário não encontrado." })
        : Results.Ok(BuildCalendarResponse(calendar));
})
.WithName("GetAccountCalendar")
.WithOpenApi();

app.MapGet("/account/notifications/preferences", async (HttpRequest request, LumaDbContext db) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var lumaUser = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(user => user.PhoneHash == PrivacyRuntime.LookupHash(account.PhoneNumber, "user.phone"));

    if (lumaUser is null)
    {
        return Results.Ok(new
        {
            available = false,
            message = "As notificações ficam disponíveis depois da primeira conversa da Luma pelo WhatsApp."
        });
    }

    var preference = await db.NotificationPreferences
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.UserId == lumaUser.Id);

    return Results.Ok(new
    {
        available = true,
        preference = BuildNotificationPreferenceResponse(preference)
    });
})
.WithName("GetNotificationPreferences")
.WithOpenApi();

app.MapPost("/account/notifications/preferences", async (
    HttpRequest request,
    NotificationPreferenceUpdate update,
    LumaDbContext db,
    NotificationPreferenceService preferences) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    if (subscription is null || subscription.PlanCode != "essencial" || subscription.Status != LumaSubscriptionStatuses.Active)
    {
        return Results.Forbid();
    }

    if (!IsValidNotificationTime(update.ReminderTime)
        || !IsValidNotificationTime(update.PeriodReminderTime)
        || !IsValidNotificationTime(update.ContraceptiveReminderTime)
        || !IsValidNotificationTime(update.SymptomCheckinTime))
    {
        return Results.BadRequest(new { message = "Horário inválido. Use algo como 08:30 ou 20h." });
    }

    var lumaUser = await db.Users.FirstOrDefaultAsync(user => user.PhoneHash == PrivacyRuntime.LookupHash(account.PhoneNumber, "user.phone"));
    if (lumaUser is null)
    {
        return Results.BadRequest(new { message = "Converse com a Luma pelo WhatsApp pelo menos uma vez antes de configurar notificações." });
    }

    var saved = await preferences.UpsertAsync(lumaUser.Id, update);
    return Results.Ok(new { preference = BuildNotificationPreferenceResponse(saved) });
})
.WithName("UpdateNotificationPreferences")
.WithOpenApi();

app.MapPost("/checkout/create-subscription", async (HttpRequest http, CheckoutCreateSubscriptionRequest request, LumaDbContext db, IConfiguration configuration) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var plan = NormalizePlan(request.PlanCode);
    var billingInterval = BillingPlanCatalog.NormalizeBillingInterval(request.BillingInterval);
    if (plan is null || billingInterval is null)
    {
        return Results.BadRequest(new { message = "Plano ou ciclo de cobrança inválido." });
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey) || string.IsNullOrWhiteSpace(stripeOptions.PublishableKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY e NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY para ativar pagamentos." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    account.StripeCustomerId = await EnsureStripeCustomerAsync(account);
    var resolvedPriceId = ResolveStripePriceId(plan, billingInterval, stripeOptions);
    var priceValidationError = await ValidateStripePriceIntervalAsync(resolvedPriceId, billingInterval);
    if (priceValidationError is not null)
    {
        return Results.BadRequest(new { message = priceValidationError });
    }

    var now = DateTimeOffset.UtcNow;
    var accountPhoneHash = PrivacyRuntime.LookupHash(account.PhoneNumber, "account.phone");
    var previousPending = await db.AccountSubscriptions
        .Where(subscription => subscription.PhoneHash == accountPhoneHash
            && subscription.Status == LumaSubscriptionStatuses.Pending)
        .ToListAsync();

    foreach (var subscription in previousPending)
    {
        subscription.Status = LumaSubscriptionStatuses.Canceled;
        subscription.CanceledAt ??= now;
        subscription.UpdatedAt = now;
    }

    var stripeSubscription = await CreateStripeSubscriptionAsync(account.StripeCustomerId, plan, billingInterval, resolvedPriceId);
    var stripePriceId = stripeSubscription.Items?.Data?.FirstOrDefault()?.Price?.Id
        ?? resolvedPriceId;
    var clientSecret = stripeSubscription.LatestInvoice?.ConfirmationSecret?.ClientSecret;
    if (string.IsNullOrWhiteSpace(clientSecret))
    {
        return Results.BadRequest(new { message = "A Stripe não retornou o client_secret da primeira cobrança." });
    }

    var pending = new AccountSubscription
    {
        AccountUserId = account.Id,
        PhoneNumber = account.PhoneNumber,
        PlanCode = plan,
        BillingInterval = billingInterval,
        Status = LumaSubscriptionStatuses.Pending,
        StripeSubscriptionId = stripeSubscription.Id,
        StripePriceId = stripePriceId,
        StartsAt = now,
        CurrentPeriodEndsAt = GetStripePeriodEnd(stripeSubscription) ?? now.AddDays(billingInterval == BillingIntervals.Annual ? 365 : 30),
        CreatedAt = now,
        UpdatedAt = now
    };

    db.AccountSubscriptions.Add(pending);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        publishableKey = stripeOptions.PublishableKey,
        clientSecret,
        stripeSubscriptionId = stripeSubscription.Id
    });
})
.WithName("CreateStripeSubscription")
.WithOpenApi();

app.MapPost("/checkout/confirm-subscription", async (
    HttpRequest http,
    CheckoutConfirmSubscriptionRequest request,
    LumaDbContext db,
    IConfiguration configuration,
    IEmailService emailService,
    ILogger<Program> logger) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var plan = NormalizePlan(request.PlanCode);
    var billingInterval = BillingPlanCatalog.NormalizeBillingInterval(request.BillingInterval);
    if (plan is null || billingInterval is null || string.IsNullOrWhiteSpace(request.StripeSubscriptionId))
    {
        return Results.BadRequest(new { message = "Assinatura inválida." });
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY para confirmar pagamentos." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    var stripeSubscription = await new SubscriptionService().GetAsync(request.StripeSubscriptionId, new SubscriptionGetOptions
    {
        Expand = ["items"]
    });
    if (stripeSubscription.CustomerId != account.StripeCustomerId)
    {
        return Results.Unauthorized();
    }

    await UpdateStripeCustomerBillingDetailsAsync(account, request.CardholderName, request.BillingCpf);

    var localSubscription = await db.AccountSubscriptions
        .FirstOrDefaultAsync(subscription =>
            subscription.AccountUserId == account.Id
            && subscription.StripeSubscriptionId == request.StripeSubscriptionId);

    if (localSubscription is null)
    {
        return Results.NotFound(new { message = "Assinatura local não encontrada." });
    }

    var previousStatus = localSubscription.Status;
    localSubscription.Status = StripeStatusToLocalStatus(stripeSubscription);
    localSubscription.PlanCode = plan;
    localSubscription.BillingInterval = billingInterval;
    localSubscription.StripePriceId = stripeSubscription.Items?.Data?.FirstOrDefault()?.Price?.Id
        ?? localSubscription.StripePriceId;
    localSubscription.CurrentPeriodEndsAt = GetStripePeriodEnd(stripeSubscription) ?? DateTimeOffset.UtcNow.AddDays(30);
    localSubscription.UpdatedAt = DateTimeOffset.UtcNow;

    if (localSubscription.Status == LumaSubscriptionStatuses.Active)
    {
        var activeSubscriptions = await db.AccountSubscriptions
            .Where(subscription => subscription.AccountUserId == account.Id
                && subscription.Id != localSubscription.Id
                && subscription.CurrentPeriodEndsAt >= DateTimeOffset.UtcNow
                && (subscription.Status == LumaSubscriptionStatuses.Active || subscription.Status == LumaSubscriptionStatuses.Canceled))
            .ToListAsync();

        foreach (var subscription in activeSubscriptions)
        {
            subscription.Status = LumaSubscriptionStatuses.Canceled;
            subscription.CanceledAt ??= DateTimeOffset.UtcNow;
            subscription.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    await db.SaveChangesAsync();
    if (localSubscription.Status == LumaSubscriptionStatuses.Active && previousStatus != LumaSubscriptionStatuses.Active)
    {
        await SendSubscriptionCreatedEmailAsync(account, localSubscription, emailService, logger);
    }

    return Results.Ok(new { subscription = BuildSubscriptionResponse(localSubscription) });
})
.WithName("ConfirmStripeSubscription")
.WithOpenApi();
app.MapPost("/account/subscription/cancel", async (HttpRequest http, LumaDbContext db) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    if (subscription is null)
    {
        return Results.NotFound(new { message = "Nenhum plano ativo encontrado." });
    }

    var now = DateTimeOffset.UtcNow;
    if (!string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
    {
        var stripeOptions = GetStripeOptions(http.HttpContext.RequestServices.GetRequiredService<IConfiguration>());
        if (!string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
        {
            StripeConfiguration.ApiKey = stripeOptions.SecretKey;
            var updated = await new SubscriptionService().UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            });

            subscription.CurrentPeriodEndsAt = GetStripePeriodEnd(updated) ?? subscription.CurrentPeriodEndsAt;
        }
    }

    subscription.Status = LumaSubscriptionStatuses.Canceled;
    subscription.CanceledAt = now;
    subscription.UpdatedAt = now;
    await db.SaveChangesAsync();

    return Results.Ok(new { subscription = BuildSubscriptionResponse(subscription) });
})
.WithName("CancelSubscription")
.WithOpenApi();

app.MapPost("/account/subscription/resume", async (HttpRequest http, LumaDbContext db) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    if (subscription is null)
    {
        return Results.NotFound(new { message = "Nenhum plano encontrado para retomar." });
    }

    var stripeOptions = GetStripeOptions(http.HttpContext.RequestServices.GetRequiredService<IConfiguration>());
    if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId) || string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
    {
        subscription.Status = LumaSubscriptionStatuses.Active;
        subscription.CanceledAt = null;
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new { subscription = BuildSubscriptionResponse(subscription) });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    var updated = await new SubscriptionService().UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
    {
        CancelAtPeriodEnd = false
    });

    subscription.Status = StripeStatusToLocalStatus(updated);
    subscription.CurrentPeriodEndsAt = GetStripePeriodEnd(updated) ?? subscription.CurrentPeriodEndsAt;
    subscription.CanceledAt = null;
    subscription.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { subscription = BuildSubscriptionResponse(subscription) });
})
.WithName("ResumeSubscription")
.WithOpenApi();

app.MapPost("/account/subscription/change-plan", async (HttpRequest http, ChangeSubscriptionPlanRequest request, LumaDbContext db) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var nextPlan = NormalizePlan(request.PlanCode);
    if (nextPlan is null)
    {
        return Results.BadRequest(new { message = "Plano inválido." });
    }

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    if (subscription is null)
    {
        return Results.NotFound(new { message = "Nenhum plano ativo encontrado." });
    }

    var nextBillingInterval = BillingPlanCatalog.NormalizeBillingInterval(request.BillingInterval)
        ?? BillingPlanCatalog.NormalizeBillingInterval(subscription.BillingInterval)
        ?? BillingIntervals.Monthly;

    if (subscription.PlanCode == nextPlan && subscription.BillingInterval == nextBillingInterval && subscription.Status == LumaSubscriptionStatuses.Active)
    {
        return Results.Ok(new { subscription = BuildSubscriptionResponse(subscription) });
    }

    var stripeOptions = GetStripeOptions(http.HttpContext.RequestServices.GetRequiredService<IConfiguration>());
    if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId) || string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
    {
        subscription.PlanCode = nextPlan;
        subscription.BillingInterval = nextBillingInterval;
        subscription.Status = LumaSubscriptionStatuses.Active;
        subscription.CanceledAt = null;
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new { subscription = BuildSubscriptionResponse(subscription) });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    var stripeSubscription = await new SubscriptionService().GetAsync(subscription.StripeSubscriptionId, new SubscriptionGetOptions
    {
        Expand = ["items"]
    });
    var item = stripeSubscription.Items?.Data?.FirstOrDefault();
    if (item is null)
    {
        return Results.BadRequest(new { message = "Não consegui localizar o item da assinatura na Stripe." });
    }

    var nextPriceId = ResolveStripePriceId(nextPlan, nextBillingInterval, stripeOptions);
    var nextPriceValidationError = await ValidateStripePriceIntervalAsync(nextPriceId, nextBillingInterval);
    if (nextPriceValidationError is not null)
    {
        return Results.BadRequest(new { message = nextPriceValidationError });
    }

    var updated = await new SubscriptionService().UpdateAsync(stripeSubscription.Id, new SubscriptionUpdateOptions
    {
        CancelAtPeriodEnd = false,
        ProrationBehavior = "create_prorations",
        Items = [new SubscriptionItemOptions { Id = item.Id, Price = nextPriceId }],
        Metadata = new Dictionary<string, string>
        {
            ["plan_code"] = nextPlan,
            ["billing_interval"] = nextBillingInterval
        }
    });

    subscription.PlanCode = nextPlan;
    subscription.BillingInterval = nextBillingInterval;
    subscription.StripePriceId = nextPriceId;
    subscription.Status = StripeStatusToLocalStatus(updated);
    subscription.CurrentPeriodEndsAt = GetStripePeriodEnd(updated) ?? subscription.CurrentPeriodEndsAt;
    subscription.CanceledAt = null;
    subscription.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { subscription = BuildSubscriptionResponse(subscription) });
})
.WithName("ChangeSubscriptionPlan")
.WithOpenApi();

app.MapPost("/account/payment-method/setup-intent", async (HttpRequest http, LumaDbContext db, IConfiguration configuration) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey) || string.IsNullOrWhiteSpace(stripeOptions.PublishableKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY e NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY para atualizar cartão." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    account.StripeCustomerId = await EnsureStripeCustomerAsync(account);
    await db.SaveChangesAsync();

    var setupIntent = await new SetupIntentService().CreateAsync(new SetupIntentCreateOptions
    {
        Customer = account.StripeCustomerId,
        PaymentMethodTypes = ["card"],
        Usage = "off_session",
        Metadata = new Dictionary<string, string>
        {
            ["account_user_id"] = account.Id.ToString()
        }
    });

    return Results.Ok(new
    {
        publishableKey = stripeOptions.PublishableKey,
        clientSecret = setupIntent.ClientSecret,
        setupIntentId = setupIntent.Id
    });
})
.WithName("CreatePaymentMethodSetupIntent")
.WithOpenApi();

app.MapPost("/account/payment-method/confirm", async (HttpRequest http, ConfirmPaymentMethodRequest request, LumaDbContext db, IConfiguration configuration) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.SetupIntentId))
    {
        return Results.BadRequest(new { message = "SetupIntent inválido." });
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY para atualizar cartão." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    var setupIntent = await new SetupIntentService().GetAsync(request.SetupIntentId);
    if (setupIntent.CustomerId != account.StripeCustomerId)
    {
        return Results.Unauthorized();
    }

    if (setupIntent.Status != "succeeded" || string.IsNullOrWhiteSpace(setupIntent.PaymentMethodId))
    {
        return Results.BadRequest(new { message = "A Stripe ainda não confirmou esse cartão." });
    }

    await UpdateStripeCustomerBillingDetailsAsync(account, request.CardholderName, request.BillingCpf, setupIntent.PaymentMethodId);

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    if (!string.IsNullOrWhiteSpace(subscription?.StripeSubscriptionId))
    {
        await new SubscriptionService().UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
        {
            DefaultPaymentMethod = setupIntent.PaymentMethodId
        });
    }

    return Results.Ok(new { ok = true });
})
.WithName("ConfirmPaymentMethod")
.WithOpenApi();

app.MapGet("/account/billing/transactions", async (HttpRequest http, LumaDbContext db, IConfiguration configuration) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey) || string.IsNullOrWhiteSpace(account.StripeCustomerId))
    {
        return Results.Ok(new { transactions = Array.Empty<object>() });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    var invoices = await new InvoiceService().ListAsync(new InvoiceListOptions
    {
        Customer = account.StripeCustomerId,
        Limit = 24
    });

    var transactions = invoices.Data
        .OrderByDescending(invoice => invoice.Created)
        .Select(invoice => new
        {
            invoice.Id,
            invoice.Number,
            invoice.Status,
            Currency = invoice.Currency?.ToUpperInvariant() ?? "BRL",
            AmountPaid = invoice.AmountPaid,
            AmountDue = invoice.AmountDue,
            invoice.HostedInvoiceUrl,
            invoice.InvoicePdf,
            invoice.Created
        });

    return Results.Ok(new { transactions });
})
.WithName("GetBillingTransactions")
.WithOpenApi();

app.MapPost("/webhooks/stripe", async (HttpRequest request, LumaDbContext db, IConfiguration configuration, IEmailService emailService, ILogger<Program> logger) =>
{
    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY para processar webhooks da Stripe." });
    }

    var payload = await new StreamReader(request.Body).ReadToEndAsync();
    var signature = request.Headers["Stripe-Signature"].ToString();

    Event stripeEvent;
    try
    {
        stripeEvent = string.IsNullOrWhiteSpace(stripeOptions.WebhookSecret)
            ? EventUtility.ParseEvent(payload)
            : EventUtility.ConstructEvent(payload, signature, stripeOptions.WebhookSecret);
    }
    catch (StripeException ex)
    {
        logger.LogWarning(ex, "Stripe webhook rejected because the signature or payload is invalid.");
        return Results.BadRequest(new { message = "Webhook da Stripe inválido." });
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Stripe webhook rejected because the JSON payload is invalid.");
        return Results.BadRequest(new { message = "Webhook da Stripe inválido." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    await HandleStripeWebhookEventAsync(stripeEvent, payload, db, stripeOptions, emailService, logger);

    return Results.Ok(new { received = true });
})
.WithName("StripeWebhook")
.WithOpenApi();

app.MapPost("/webhooks/twilio/whatsapp", async (
    HttpRequest request,
    ConversationService conversations,
    ConversationScopeDetector scopeDetector,
    MessageIngressGuard ingressGuard,
    IWhatsAppAudioTranscriptionService audioTranscription,
    IWhatsAppTypingIndicatorSender typingIndicators,
    LumaDbContext db,
    IConfiguration configuration) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Twilio webhooks must use application/x-www-form-urlencoded.");
    }

    var form = await request.ReadFormAsync();
    var from = form["From"].ToString();
    var body = form["Body"].ToString();
    var messageSid = form["MessageSid"].ToString();

    if (string.IsNullOrWhiteSpace(from))
    {
        return Results.BadRequest("Missing Twilio From field.");
    }

    var scope = scopeDetector.DetectTwilio(form);
    if (scope.IsGroup)
    {
        db.BlockedConversations.Add(new BlockedConversation
        {
            Provider = "twilio",
            From = PhoneNumber.Normalize(from),
            Reason = scope.Reason
        });
        await db.SaveChangesAsync();

        return TwilioXmlReply("Oi, eu sou a Luma. Por privacidade, eu só consigo conversar em atendimentos individuais. Se quiser continuar, me chame no privado.");
    }

    var normalizedFrom = PhoneNumber.Normalize(from);
    var decision = await ingressGuard.BeginAsync("twilio", normalizedFrom, string.IsNullOrWhiteSpace(messageSid) ? null : messageSid);
    if (!decision.AllowProcessing)
    {
        return string.IsNullOrWhiteSpace(decision.Reply)
            ? TwilioEmptyReply()
            : TwilioXmlReply(decision.Reply);
    }

    await using var lease = decision.Lease;
    _ = typingIndicators.TrySendAsync(messageSid, CancellationToken.None);

    if (string.IsNullOrWhiteSpace(body))
    {
        if (WhatsAppAudioTranscriptionService.HasAudioMedia(form))
        {
            if (!await HasActiveSubscriptionAccessAsync(db, normalizedFrom))
            {
                return TwilioXmlReply("Olá! Para conversar com a Luma pelo WhatsApp, é preciso ter um plano ativo vinculado a este número. Acesse sua conta no site, escolha um plano e depois me chame por aqui novamente.");
            }

            if (!await HasEssentialFeatureAccessAsync(db, normalizedFrom))
            {
                return TwilioXmlReply(await BuildFeatureUpgradeReplyAsync(db, configuration, normalizedFrom, "mensagens por áudio"));
            }
        }

        var audio = await audioTranscription.TryTranscribeAsync(form, request.HttpContext.RequestAborted);
        if (audio.Attempted && audio.Success && !string.IsNullOrWhiteSpace(audio.Text))
        {
            body = audio.Text;
        }
        else if (audio.Attempted)
        {
            return TwilioXmlReply("Eu recebi seu áudio, mas não consegui entender com segurança agora. Pode tentar mandar de novo ou escrever a mensagem em texto?");
        }
        else if (int.TryParse(form["NumMedia"].ToString(), out var mediaCount) && mediaCount > 0)
        {
            return TwilioXmlReply("Recebi seu arquivo. Por enquanto, consigo interpretar mensagens de texto e áudios curtos pelo WhatsApp.");
        }
    }

    var reply = await conversations.HandleIncomingMessageRichAsync(new IncomingMessage(
        Provider: "twilio",
        From: normalizedFrom,
        Body: body,
        ProviderMessageId: string.IsNullOrWhiteSpace(messageSid) ? null : messageSid));

    return TwilioXmlReply(reply.Body, reply.MediaUrl);
})
.WithName("TwilioWhatsAppWebhook")
.WithOpenApi();

app.MapPost("/dev/messages", async (DevIncomingMessage message, ConversationService conversations) =>
{
    var reply = await conversations.HandleIncomingMessageAsync(new IncomingMessage(
        Provider: "dev",
        From: PhoneNumber.Normalize(message.From),
        Body: message.Body,
        ProviderMessageId: null));

    return Results.Ok(new { reply });
})
.WithName("DevIncomingMessage")
.WithOpenApi();

app.MapPost("/dev/notifications/run", async (NotificationProcessor processor) =>
{
    var processed = await processor.RunDueNotificationsAsync();
    return Results.Ok(new { processed });
})
.WithName("RunDueNotifications")
.WithOpenApi();

app.MapGet("/admin/users", async (LumaDbContext db) =>
{
    var users = await db.Users
        .AsNoTracking()
        .Include(user => user.Preference)
        .OrderByDescending(user => user.CreatedAt)
        .Select(user => new
        {
            user.Id,
            phone = PhoneNumber.Mask(user.PhoneNumber),
            user.DisplayName,
            user.OnboardingStep,
            user.IsAdultConfirmed,
            user.ConsentAcceptedAt,
            user.CreatedAt,
            preference = user.Preference == null
                ? null
                : new
                {
                    user.Preference.AverageCycleLength,
                    user.Preference.AveragePeriodLength,
                    user.Preference.LastPeriodStartDate,
                    user.Preference.UsesHormonalContraceptive,
                    user.Preference.ContraceptiveType,
                    user.Preference.RemindersEnabled
                }
        })
        .ToListAsync();

    return Results.Ok(users);
})
.WithName("AdminUsers")
.WithOpenApi();

app.MapGet("/admin/users/{id:guid}/events", async (Guid id, LumaDbContext db) =>
{
    var events = await db.CycleEvents
        .AsNoTracking()
        .Where(ev => ev.UserId == id)
        .OrderByDescending(ev => ev.Date)
        .ThenByDescending(ev => ev.CreatedAt)
        .Select(ev => new
        {
            ev.Id,
            ev.Type,
            ev.Date,
            ev.Source,
            ev.MetadataJson,
            ev.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(events);
})
.WithName("AdminUserEvents")
.WithOpenApi();

app.Run();

static IResult TwilioXmlReply(string reply, string? mediaUrl = null)
{
    var message = new XElement("Message", reply);
    if (!string.IsNullOrWhiteSpace(mediaUrl))
    {
        message.Add(new XElement("Media", mediaUrl));
    }

    var twiml = new XDocument(
        new XElement("Response",
            message));

    return Results.Text(twiml.ToString(SaveOptions.DisableFormatting), "application/xml", Encoding.UTF8);
}

static IResult TwilioEmptyReply()
{
    var twiml = new XDocument(new XElement("Response"));
    return Results.Text(twiml.ToString(SaveOptions.DisableFormatting), "application/xml", Encoding.UTF8);
}

static async Task<bool> HasEssentialFeatureAccessAsync(LumaDbContext db, string phone)
{
    var now = DateTimeOffset.UtcNow;
    var phoneHash = PrivacyRuntime.LookupHash(phone, "account.phone");
    return await db.AccountSubscriptions.AnyAsync(subscription =>
        subscription.PhoneHash == phoneHash
        && subscription.PlanCode == "essencial"
        && subscription.CurrentPeriodEndsAt >= now
        && (subscription.Status == LumaSubscriptionStatuses.Active || subscription.Status == LumaSubscriptionStatuses.Canceled));
}

static async Task<bool> HasActiveSubscriptionAccessAsync(LumaDbContext db, string phone)
{
    var now = DateTimeOffset.UtcNow;
    var phoneHash = PrivacyRuntime.LookupHash(phone, "account.phone");
    return await db.AccountSubscriptions.AnyAsync(subscription =>
        subscription.PhoneHash == phoneHash
        && subscription.CurrentPeriodEndsAt >= now
        && (subscription.Status == LumaSubscriptionStatuses.Active || subscription.Status == LumaSubscriptionStatuses.Canceled));
}

static async Task<string> BuildFeatureUpgradeReplyAsync(LumaDbContext db, IConfiguration configuration, string phone, string feature)
{
    var account = await db.AccountUsers
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.PhoneHash == PrivacyRuntime.LookupHash(phone, "account.phone"));

    var lumaUser = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(user => user.PhoneHash == PrivacyRuntime.LookupHash(phone, "user.phone"));

    var baseUrl = (configuration.GetValue<string>("Luma:WebBaseUrl") ?? "http://localhost:3000").TrimEnd('/');
    var profilePath = account is null ? "/perfil" : $"/perfil/{account.Id}";
    var prefix = string.IsNullOrWhiteSpace(lumaUser?.DisplayName) ? string.Empty : $"{lumaUser.DisplayName}, ";

    return $"{prefix}seu plano atual não oferece {feature}. Você pode atualizar para o Essencial quando quiser no seu painel: {baseUrl}{profilePath}";
}

static async Task EnsureRuntimeSchemaAsync(LumaDbContext db)
{
    if (!db.Database.IsNpgsql())
    {
        return;
    }

    await db.Database.ExecuteSqlRawAsync("""
CREATE TABLE IF NOT EXISTS pregnancies (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "Status" character varying(32) NOT NULL,
    "StartReference" character varying(64),
    "LastPeriodDate" date,
    "GestationalWeeksAtRegistration" integer,
    "EstimatedDueDate" date,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_pregnancies_UserId_Status" ON pregnancies ("UserId", "Status");
CREATE TABLE IF NOT EXISTS pending_intents (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "Intent" character varying(64) NOT NULL,
    "Date" date,
    "RequiredBeforeAction" character varying(64) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "PayloadJson" jsonb NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone
);
CREATE INDEX IF NOT EXISTS "IX_pending_intents_UserId_Status_CreatedAt" ON pending_intents ("UserId", "Status", "CreatedAt");
CREATE TABLE IF NOT EXISTS account_users (
    "Id" uuid PRIMARY KEY,
    "Email" text NOT NULL,
    "EmailHash" character varying(128) NOT NULL DEFAULT '',
    "Cpf" text NOT NULL,
    "CpfHash" character varying(128) NOT NULL DEFAULT '',
    "FullName" text NOT NULL,
    "PasswordHash" character varying(512) NOT NULL,
    "PhoneNumber" text NOT NULL,
    "PhoneHash" character varying(128) NOT NULL DEFAULT '',
    "PhoneVerifiedAt" timestamp with time zone,
    "StripeCustomerId" character varying(128),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_users_Email" ON account_users ("Email");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_users_Cpf" ON account_users ("Cpf");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_users_PhoneNumber" ON account_users ("PhoneNumber");
CREATE TABLE IF NOT EXISTS account_sessions (
    "Id" uuid PRIMARY KEY,
    "AccountUserId" uuid NOT NULL REFERENCES account_users ("Id") ON DELETE CASCADE,
    "TokenHash" character varying(128) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_sessions_TokenHash" ON account_sessions ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_account_sessions_AccountUserId_ExpiresAt" ON account_sessions ("AccountUserId", "ExpiresAt");
CREATE TABLE IF NOT EXISTS account_phone_verification_codes (
    "Id" uuid PRIMARY KEY,
    "AccountUserId" uuid NOT NULL REFERENCES account_users ("Id") ON DELETE CASCADE,
    "PhoneNumber" text NOT NULL,
    "PhoneHash" character varying(128) NOT NULL DEFAULT '',
    "Purpose" character varying(32) NOT NULL,
    "CodeHash" character varying(128) NOT NULL,
    "Attempts" integer NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "ConsumedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_account_phone_verification_codes_AccountUserId_PhoneHash_Purpose_ExpiresAt" ON account_phone_verification_codes ("AccountUserId", "PhoneHash", "Purpose", "ExpiresAt");
CREATE TABLE IF NOT EXISTS account_subscriptions (
    "Id" uuid PRIMARY KEY,
    "AccountUserId" uuid NOT NULL REFERENCES account_users ("Id") ON DELETE CASCADE,
    "PhoneNumber" text NOT NULL,
    "PhoneHash" character varying(128) NOT NULL DEFAULT '',
    "PlanCode" character varying(32) NOT NULL,
    "BillingInterval" character varying(32) NOT NULL DEFAULT 'monthly',
    "Status" character varying(32) NOT NULL,
    "StripeSubscriptionId" character varying(128),
    "StripePriceId" character varying(128),
    "StartsAt" timestamp with time zone NOT NULL,
    "CurrentPeriodEndsAt" timestamp with time zone NOT NULL,
    "CanceledAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_account_subscriptions_PhoneNumber_Status_CurrentPeriodEndsAt" ON account_subscriptions ("PhoneNumber", "Status", "CurrentPeriodEndsAt");
CREATE TABLE IF NOT EXISTS password_reset_tokens (
    "Id" uuid PRIMARY KEY,
    "AccountUserId" uuid NOT NULL REFERENCES account_users ("Id") ON DELETE CASCADE,
    "TokenHash" character varying(128) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone,
    "RequestIp" character varying(128),
    "UserAgent" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_password_reset_tokens_TokenHash" ON password_reset_tokens ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_password_reset_tokens_AccountUserId_ExpiresAt" ON password_reset_tokens ("AccountUserId", "ExpiresAt");
CREATE TABLE IF NOT EXISTS email_logs (
    "Id" uuid PRIMARY KEY,
    "To" character varying(320) NOT NULL,
    "TemplateId" character varying(128) NOT NULL,
    "Provider" character varying(32) NOT NULL,
    "ProviderMessageId" character varying(128),
    "Status" character varying(32) NOT NULL,
    "Error" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_email_logs_To_CreatedAt" ON email_logs ("To", "CreatedAt");
CREATE TABLE IF NOT EXISTS support_requests (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES account_users ("Id") ON DELETE CASCADE,
    "UserName" character varying(1024) NOT NULL,
    "UserEmail" character varying(1024) NOT NULL,
    "Subject" character varying(200) NOT NULL,
    "Description" character varying(5000) NOT NULL,
    "AttachmentCount" integer NOT NULL,
    "Status" character varying(32) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_support_requests_UserId_CreatedAt" ON support_requests ("UserId", "CreatedAt");
CREATE TABLE IF NOT EXISTS support_request_attachment_metadata (
    "Id" uuid PRIMARY KEY,
    "SupportRequestId" uuid NOT NULL REFERENCES support_requests ("Id") ON DELETE CASCADE,
    "FileName" character varying(255) NOT NULL,
    "ContentType" character varying(128) NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_support_request_attachment_metadata_SupportRequestId" ON support_request_attachment_metadata ("SupportRequestId");
CREATE TABLE IF NOT EXISTS notification_preferences (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
    "PeriodReminderEnabled" boolean NOT NULL,
    "ContraceptiveReminderEnabled" boolean NOT NULL,
    "SymptomCheckinEnabled" boolean NOT NULL,
    "ReminderTime" time without time zone NOT NULL,
    "PeriodReminderTime" time without time zone NOT NULL DEFAULT TIME '09:00',
    "ContraceptiveReminderTime" time without time zone NOT NULL DEFAULT TIME '09:00',
    "SymptomCheckinTime" time without time zone NOT NULL DEFAULT TIME '09:00',
    "TimeZone" character varying(64) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_notification_preferences_UserId" ON notification_preferences ("UserId");
CREATE TABLE IF NOT EXISTS notification_deliveries (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
    "AccountSubscriptionId" uuid,
    "Type" character varying(64) NOT NULL,
    "ScheduledForDate" date NOT NULL,
    "ScheduledFor" timestamp with time zone NOT NULL,
    "SentAt" timestamp with time zone,
    "Status" character varying(32) NOT NULL,
    "Provider" character varying(32),
    "ProviderMessageId" character varying(128),
    "ErrorMessage" character varying(512),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_notification_deliveries_UserId_Type_ScheduledForDate" ON notification_deliveries ("UserId", "Type", "ScheduledForDate");
CREATE TABLE IF NOT EXISTS blocked_conversations (
    "Id" uuid PRIMARY KEY,
    "Provider" character varying(32) NOT NULL,
    "From" text NOT NULL,
    "FromHash" character varying(128) NOT NULL DEFAULT '',
    "Reason" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_blocked_conversations_Provider_CreatedAt" ON blocked_conversations ("Provider", "CreatedAt");
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "StripeCustomerId" character varying(128);
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "PhoneVerifiedAt" timestamp with time zone;
ALTER TABLE notification_preferences ADD COLUMN IF NOT EXISTS "PeriodReminderTime" time without time zone NOT NULL DEFAULT TIME '09:00';
ALTER TABLE notification_preferences ADD COLUMN IF NOT EXISTS "ContraceptiveReminderTime" time without time zone NOT NULL DEFAULT TIME '09:00';
ALTER TABLE notification_preferences ADD COLUMN IF NOT EXISTS "SymptomCheckinTime" time without time zone NOT NULL DEFAULT TIME '09:00';
UPDATE notification_preferences SET "PeriodReminderTime" = "ReminderTime" WHERE "PeriodReminderTime" = TIME '09:00' AND "ReminderTime" <> TIME '09:00';
UPDATE notification_preferences SET "ContraceptiveReminderTime" = "ReminderTime" WHERE "ContraceptiveReminderTime" = TIME '09:00' AND "ReminderTime" <> TIME '09:00';
UPDATE notification_preferences SET "SymptomCheckinTime" = "ReminderTime" WHERE "SymptomCheckinTime" = TIME '09:00' AND "ReminderTime" <> TIME '09:00';
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "EmailHash" character varying(128) NOT NULL DEFAULT '';
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "CpfHash" character varying(128) NOT NULL DEFAULT '';
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "PhoneHash" character varying(128) NOT NULL DEFAULT '';
ALTER TABLE account_users ALTER COLUMN "Email" TYPE text;
ALTER TABLE account_users ALTER COLUMN "Cpf" TYPE text;
ALTER TABLE account_users ALTER COLUMN "FullName" TYPE text;
ALTER TABLE account_users ALTER COLUMN "PhoneNumber" TYPE text;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_users_EmailHash" ON account_users ("EmailHash") WHERE "EmailHash" <> '';
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_users_CpfHash" ON account_users ("CpfHash") WHERE "CpfHash" <> '';
CREATE UNIQUE INDEX IF NOT EXISTS "IX_account_users_PhoneHash" ON account_users ("PhoneHash") WHERE "PhoneHash" <> '';
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "StripeSubscriptionId" character varying(128);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "StripePriceId" character varying(128);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "PhoneHash" character varying(128) NOT NULL DEFAULT '';
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "BillingInterval" character varying(32) NOT NULL DEFAULT 'monthly';
ALTER TABLE account_subscriptions ALTER COLUMN "PhoneNumber" TYPE text;
CREATE INDEX IF NOT EXISTS "IX_account_subscriptions_PhoneHash_Status_CurrentPeriodEndsAt" ON account_subscriptions ("PhoneHash", "Status", "CurrentPeriodEndsAt");
ALTER TABLE users ADD COLUMN IF NOT EXISTS "PhoneHash" character varying(128) NOT NULL DEFAULT '';
ALTER TABLE users ALTER COLUMN "PhoneNumber" TYPE text;
ALTER TABLE users ALTER COLUMN "DisplayName" TYPE text;
ALTER TABLE user_preferences ALTER COLUMN "ContraceptiveType" TYPE text;
ALTER TABLE pregnancies ALTER COLUMN "StartReference" TYPE text;
ALTER TABLE messages ALTER COLUMN "Body" TYPE text;
ALTER TABLE blocked_conversations ADD COLUMN IF NOT EXISTS "FromHash" character varying(128) NOT NULL DEFAULT '';
ALTER TABLE blocked_conversations ALTER COLUMN "From" TYPE text;
ALTER TABLE blocked_conversations ALTER COLUMN "Reason" TYPE text;
UPDATE account_users
SET "PhoneNumber" = '+55' || substring("PhoneNumber" from 2)
WHERE "PhoneNumber" ~ '^\+[1-9][0-9]{{9,10}}$' AND "PhoneNumber" NOT LIKE '+55%';
UPDATE account_subscriptions
SET "PhoneNumber" = '+55' || substring("PhoneNumber" from 2)
WHERE "PhoneNumber" ~ '^\+[1-9][0-9]{{9,10}}$' AND "PhoneNumber" NOT LIKE '+55%';
UPDATE users
SET "PhoneNumber" = '+55' || substring("PhoneNumber" from 2)
WHERE "PhoneNumber" ~ '^\+[1-9][0-9]{{9,10}}$' AND "PhoneNumber" NOT LIKE '+55%';
""");

    await BackfillPrivacyIndexesAsync(db);
}

static async Task BackfillPrivacyIndexesAsync(LumaDbContext db)
{
    foreach (var account in await db.AccountUsers.ToListAsync())
    {
        db.Entry(account).State = EntityState.Modified;
    }

    foreach (var user in await db.Users.ToListAsync())
    {
        db.Entry(user).State = EntityState.Modified;
    }

    foreach (var subscription in await db.AccountSubscriptions.ToListAsync())
    {
        db.Entry(subscription).State = EntityState.Modified;
    }

    foreach (var blocked in await db.BlockedConversations.ToListAsync())
    {
        db.Entry(blocked).State = EntityState.Modified;
    }

    foreach (var preference in await db.UserPreferences.Where(item => item.ContraceptiveType != null).ToListAsync())
    {
        db.Entry(preference).State = EntityState.Modified;
    }

    foreach (var pregnancy in await db.Pregnancies.Where(item => item.StartReference != null).ToListAsync())
    {
        db.Entry(pregnancy).State = EntityState.Modified;
    }

    foreach (var message in await db.Messages.Where(item => item.Body != null).ToListAsync())
    {
        db.Entry(message).State = EntityState.Modified;
    }

    foreach (var pending in await db.PendingIntents.ToListAsync())
    {
        db.Entry(pending).State = EntityState.Modified;
    }

    foreach (var cycleEvent in await db.CycleEvents.ToListAsync())
    {
        db.Entry(cycleEvent).State = EntityState.Modified;
    }

    await db.SaveChangesAsync();
}

static async Task<AccountUser?> GetAuthenticatedAccountAsync(HttpRequest request, LumaDbContext db)
{
    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = header["Bearer ".Length..].Trim();
    if (string.IsNullOrWhiteSpace(token))
    {
        return null;
    }

    var signingKey = request.HttpContext.RequestServices
        .GetRequiredService<IConfiguration>()
        .GetValue<string>("Luma:JwtSigningKey")
        ?? "luma-dev-jwt-signing-key-change-me-please";
    var accountId = AccountSecurity.ValidateJwt(token, signingKey);
    if (accountId is null)
    {
        return null;
    }

    return await db.AccountUsers.FirstOrDefaultAsync(account => account.Id == accountId.Value);
}

static async Task<AccountSubscription?> GetVisibleSubscriptionAsync(LumaDbContext db, string phoneNumber)
{
    var now = DateTimeOffset.UtcNow;
    var phoneHash = PrivacyRuntime.LookupHash(phoneNumber, "account.phone");
    return await db.AccountSubscriptions
        .Where(subscription => subscription.PhoneHash == phoneHash
            && subscription.CurrentPeriodEndsAt >= now
            && (subscription.Status == LumaSubscriptionStatuses.Active || subscription.Status == LumaSubscriptionStatuses.Canceled))
        .OrderByDescending(subscription => subscription.CurrentPeriodEndsAt)
        .FirstOrDefaultAsync();
}

static StripeBillingOptions GetStripeOptions(IConfiguration configuration)
{
    return new StripeBillingOptions
    {
        SecretKey = configuration.GetValue<string>("Stripe:SecretKey") ?? string.Empty,
        PublishableKey = configuration.GetValue<string>("Stripe:PublishableKey") ?? string.Empty,
        BasicPriceId = configuration.GetValue<string>("Stripe:BasicPriceId") ?? string.Empty,
        EssentialPriceId = configuration.GetValue<string>("Stripe:EssentialPriceId") ?? string.Empty,
        BasicMonthlyPriceId = configuration.GetValue<string>("Stripe:BasicMonthlyPriceId") ?? string.Empty,
        BasicAnnualPriceId = configuration.GetValue<string>("Stripe:BasicAnnualPriceId") ?? string.Empty,
        EssentialMonthlyPriceId = configuration.GetValue<string>("Stripe:EssentialMonthlyPriceId") ?? string.Empty,
        EssentialAnnualPriceId = configuration.GetValue<string>("Stripe:EssentialAnnualPriceId") ?? string.Empty,
        WebhookSecret = configuration.GetValue<string>("Stripe:WebhookSecret") ?? string.Empty
    };
}

static async Task HandleStripeWebhookEventAsync(
    Event stripeEvent,
    string payload,
    LumaDbContext db,
    StripeBillingOptions stripeOptions,
    IEmailService emailService,
    ILogger logger)
{
    switch (stripeEvent.Type)
    {
        case EventTypes.CustomerSubscriptionCreated:
        case EventTypes.CustomerSubscriptionUpdated:
        case EventTypes.CustomerSubscriptionDeleted:
            if (stripeEvent.Data.Object is StripeSubscription subscription)
            {
                await SyncStripeSubscriptionAsync(db, subscription, stripeOptions, emailService, logger);
            }
            break;

        case EventTypes.InvoicePaymentSucceeded:
        case EventTypes.InvoicePaymentFailed:
            var subscriptionId = ExtractStripeSubscriptionIdFromInvoicePayload(payload);
            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                var invoiceSubscription = await new SubscriptionService().GetAsync(subscriptionId, new SubscriptionGetOptions
                {
                    Expand = ["items"]
                });
                await SyncStripeSubscriptionAsync(db, invoiceSubscription, stripeOptions, emailService, logger);
            }
            break;

        default:
            logger.LogInformation("Ignoring Stripe webhook event {EventType}.", stripeEvent.Type);
            break;
    }
}

static async Task SyncStripeSubscriptionAsync(
    LumaDbContext db,
    StripeSubscription stripeSubscription,
    StripeBillingOptions stripeOptions,
    IEmailService emailService,
    ILogger logger)
{
    var localSubscription = await db.AccountSubscriptions
        .Include(subscription => subscription.AccountUser)
        .FirstOrDefaultAsync(subscription => subscription.StripeSubscriptionId == stripeSubscription.Id);
    var previousStatus = localSubscription?.Status;

    if (localSubscription is null)
    {
        var account = await db.AccountUsers
            .FirstOrDefaultAsync(account => account.StripeCustomerId == stripeSubscription.CustomerId);

        if (account is null)
        {
            logger.LogInformation("Stripe subscription {SubscriptionId} has no matching Luma account.", stripeSubscription.Id);
            return;
        }

        var (planCode, billingInterval, priceId) = ResolvePlanFromStripeSubscription(stripeSubscription, stripeOptions);
        if (planCode is null)
        {
            logger.LogWarning("Stripe subscription {SubscriptionId} has no recognizable Luma plan.", stripeSubscription.Id);
            return;
        }

        localSubscription = new AccountSubscription
        {
            AccountUserId = account.Id,
            PhoneNumber = account.PhoneNumber,
            PlanCode = planCode,
            BillingInterval = billingInterval ?? BillingIntervals.Monthly,
            StripeSubscriptionId = stripeSubscription.Id,
            StripePriceId = priceId,
            StartsAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AccountSubscriptions.Add(localSubscription);
    }

    var resolved = ResolvePlanFromStripeSubscription(stripeSubscription, stripeOptions);
    var now = DateTimeOffset.UtcNow;
    if (resolved.PlanCode is not null)
    {
        localSubscription.PlanCode = resolved.PlanCode;
    }

    if (resolved.BillingInterval is not null)
    {
        localSubscription.BillingInterval = resolved.BillingInterval;
    }

    localSubscription.StripePriceId = resolved.PriceId ?? localSubscription.StripePriceId;
    localSubscription.Status = StripeStatusToLocalStatus(stripeSubscription);
    localSubscription.CurrentPeriodEndsAt = GetStripePeriodEnd(stripeSubscription) ?? localSubscription.CurrentPeriodEndsAt;
    localSubscription.CanceledAt = localSubscription.Status == LumaSubscriptionStatuses.Canceled
        ? localSubscription.CanceledAt ?? now
        : null;
    localSubscription.UpdatedAt = now;

    if (localSubscription.Status == LumaSubscriptionStatuses.Active)
    {
        var otherSubscriptions = await db.AccountSubscriptions
            .Where(subscription => subscription.AccountUserId == localSubscription.AccountUserId
                && subscription.Id != localSubscription.Id
                && subscription.CurrentPeriodEndsAt >= now
                && (subscription.Status == LumaSubscriptionStatuses.Active || subscription.Status == LumaSubscriptionStatuses.Canceled))
            .ToListAsync();

        foreach (var subscription in otherSubscriptions)
        {
            subscription.Status = LumaSubscriptionStatuses.Canceled;
            subscription.CanceledAt ??= now;
            subscription.UpdatedAt = now;
        }
    }

    await db.SaveChangesAsync();

    if (localSubscription.Status == LumaSubscriptionStatuses.Active && previousStatus != LumaSubscriptionStatuses.Active)
    {
        var account = localSubscription.AccountUser
            ?? await db.AccountUsers.FirstOrDefaultAsync(item => item.Id == localSubscription.AccountUserId);
        if (account is not null)
        {
            await SendSubscriptionCreatedEmailAsync(account, localSubscription, emailService, logger);
        }
    }
}

static async Task SendSubscriptionCreatedEmailAsync(
    AccountUser account,
    AccountSubscription subscription,
    IEmailService emailService,
    ILogger logger)
{
    var planName = subscription.PlanCode == "essencial" ? "Essencial" : "Básico";
    var result = await emailService.SendSubscriptionCreatedEmailAsync(account.Email, account.FullName, planName);
    if (!result.Success)
    {
        logger.LogWarning("subscription_created_email_failed for account {AccountId}: {Error}", account.Id, result.ErrorMessage);
    }
}

static string? ExtractStripeSubscriptionIdFromInvoicePayload(string payload)
{
    using var document = JsonDocument.Parse(payload);
    if (!document.RootElement.TryGetProperty("data", out var data)
        || !data.TryGetProperty("object", out var invoice))
    {
        return null;
    }

    if (invoice.TryGetProperty("subscription", out var legacySubscription)
        && legacySubscription.ValueKind == JsonValueKind.String)
    {
        return legacySubscription.GetString();
    }

    if (invoice.TryGetProperty("parent", out var parent)
        && parent.TryGetProperty("subscription_details", out var subscriptionDetails)
        && subscriptionDetails.TryGetProperty("subscription", out var subscription)
        && subscription.ValueKind == JsonValueKind.String)
    {
        return subscription.GetString();
    }

    return null;
}

static (string? PlanCode, string? BillingInterval, string? PriceId) ResolvePlanFromStripeSubscription(StripeSubscription subscription, StripeBillingOptions stripeOptions)
{
    var priceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
    var metadataBillingInterval = subscription.Metadata is not null && subscription.Metadata.TryGetValue("billing_interval", out var billing)
        ? BillingPlanCatalog.NormalizeBillingInterval(billing)
        : null;

    if (subscription.Metadata is not null && subscription.Metadata.TryGetValue("plan_code", out var metadataPlan))
    {
        var plan = NormalizePlan(metadataPlan);
        if (plan is not null)
        {
            return (plan, metadataBillingInterval ?? BillingPlanCatalog.ResolvePlanFromPriceId(priceId, stripeOptions).BillingInterval, priceId);
        }
    }

    var resolved = BillingPlanCatalog.ResolvePlanFromPriceId(priceId, stripeOptions);
    return (resolved.PlanCode, resolved.BillingInterval, priceId);
}

static async Task<string> EnsureStripeCustomerAsync(AccountUser account)
{
    if (!string.IsNullOrWhiteSpace(account.StripeCustomerId))
    {
        try
        {
            var existingCustomer = await new CustomerService().GetAsync(account.StripeCustomerId);
            if (existingCustomer is not null && existingCustomer.Deleted != true)
            {
                return account.StripeCustomerId;
            }
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing" || ex.Message.Contains("No such customer", StringComparison.OrdinalIgnoreCase))
        {
            account.StripeCustomerId = null;
        }
    }

    var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
    {
        Email = account.Email,
        Name = account.FullName,
        Phone = account.PhoneNumber,
        Metadata = new Dictionary<string, string>
        {
            ["account_user_id"] = account.Id.ToString()
        }
    });

    return customer.Id;
}

static async Task UpdateStripeCustomerBillingDetailsAsync(
    AccountUser account,
    string? cardholderName = null,
    string? billingCpf = null,
    string? defaultPaymentMethodId = null)
{
    if (string.IsNullOrWhiteSpace(account.StripeCustomerId))
    {
        return;
    }

    var cpf = string.IsNullOrWhiteSpace(billingCpf)
        ? account.Cpf
        : AccountInputNormalizer.OnlyDigits(billingCpf);
    var name = string.IsNullOrWhiteSpace(cardholderName)
        ? account.FullName
        : cardholderName.Trim();

    var options = new CustomerUpdateOptions
    {
        Name = name,
        Email = account.Email,
        Phone = account.PhoneNumber,
        Metadata = new Dictionary<string, string>
        {
            ["account_user_id"] = account.Id.ToString(),
            ["cpf"] = cpf
        }
    };

    if (!string.IsNullOrWhiteSpace(defaultPaymentMethodId))
    {
        options.InvoiceSettings = new CustomerInvoiceSettingsOptions
        {
            DefaultPaymentMethod = defaultPaymentMethodId
        };
    }

    await new CustomerService().UpdateAsync(account.StripeCustomerId, options);
}

static async Task<Subscription> CreateStripeSubscriptionAsync(string customerId, string planCode, string billingInterval, string priceId)
{
    return await new SubscriptionService().CreateAsync(new SubscriptionCreateOptions
    {
        Customer = customerId,
        Items = [new SubscriptionItemOptions { Price = priceId }],
        PaymentBehavior = "default_incomplete",
        PaymentSettings = new SubscriptionPaymentSettingsOptions
        {
            SaveDefaultPaymentMethod = "on_subscription"
        },
        Metadata = new Dictionary<string, string>
        {
            ["plan_code"] = planCode,
            ["billing_interval"] = billingInterval
        },
        Expand = ["latest_invoice.confirmation_secret", "items"]
    });
}

static async Task<string?> ValidateStripePriceIntervalAsync(string priceId, string billingInterval)
{
    var price = await new PriceService().GetAsync(priceId);
    var expectedInterval = billingInterval == BillingIntervals.Annual ? "year" : "month";
    var actualInterval = price.Recurring?.Interval;
    if (actualInterval == expectedInterval)
    {
        return null;
    }

    var expectedLabel = billingInterval == BillingIntervals.Annual ? "anual" : "mensal";
    var actualLabel = actualInterval switch
    {
        "day" => "diária",
        "week" => "semanal",
        "month" => "mensal",
        "year" => "anual",
        _ => "sem recorrência"
    };

    return $"O preço da Stripe para o ciclo {expectedLabel} está configurado como cobrança {actualLabel}. Ajuste o Price ID na Stripe antes de vender esse plano.";
}

static string ResolveStripePriceId(string planCode, string billingInterval, StripeBillingOptions options)
{
    return BillingPlanCatalog.ResolvePriceId(planCode, billingInterval, options);
}

static DateTimeOffset? GetStripePeriodEnd(Subscription subscription)
{
    return subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
}

static string StripeStatusToLocalStatus(StripeSubscription subscription)
{
    if (subscription.Status is "active" or "trialing")
    {
        return subscription.CancelAtPeriodEnd
            ? LumaSubscriptionStatuses.Canceled
            : LumaSubscriptionStatuses.Active;
    }

    return subscription.Status == "canceled"
        ? LumaSubscriptionStatuses.Canceled
        : LumaSubscriptionStatuses.Pending;
}

static object BuildAccountAuthResponse(AccountUser account, IConfiguration configuration, string? phoneVerificationMessage = null)
{
    var signingKey = configuration.GetValue<string>("Luma:JwtSigningKey")
        ?? "luma-dev-jwt-signing-key-change-me-please";
    var token = AccountSecurity.CreateJwt(
        account.Id,
        account.Email,
        account.PhoneNumber,
        signingKey,
        DateTimeOffset.UtcNow.AddDays(30));

    return new
    {
        token,
        user = BuildAccountUserResponse(account),
        phoneVerificationRequired = account.PhoneVerifiedAt is null,
        phoneVerificationMessage
    };
}

static object BuildAccountUserResponse(AccountUser account)
{
    return new
    {
        account.Id,
        account.Email,
        account.Cpf,
        account.FullName,
        account.PhoneNumber,
        account.PhoneVerifiedAt,
        account.CreatedAt
    };
}

static object BuildSubscriptionResponse(AccountSubscription subscription)
{
    return new
    {
        subscription.Id,
        subscription.PlanCode,
        planName = subscription.PlanCode == "essencial" ? "Essencial" : "Básico",
        subscription.BillingInterval,
        billingLabel = subscription.BillingInterval == BillingIntervals.Annual ? "Anual" : "Mensal",
        subscription.Status,
        subscription.StripeSubscriptionId,
        subscription.StripePriceId,
        subscription.StartsAt,
        subscription.CurrentPeriodEndsAt,
        subscription.CanceledAt
    };
}

static object BuildCalendarResponse(CycleCalendar calendar)
{
    return new
    {
        month = calendar.Month.ToString(),
        summary = new
        {
            calendar.Summary.LastPeriodDate,
            calendar.Summary.NextPeriodDate,
            calendar.Summary.ActivePregnancy,
            calendar.Summary.EstimatedDueDate
        },
        days = calendar.Days.Select(day => new
        {
            day.Date,
            items = day.Items.Select(item => new
            {
                item.Type,
                item.Label,
                item.IsPrediction
            })
        })
    };
}

static object BuildNotificationPreferenceResponse(NotificationPreference? preference)
{
    return new
    {
        periodReminderEnabled = preference?.PeriodReminderEnabled ?? false,
        contraceptiveReminderEnabled = preference?.ContraceptiveReminderEnabled ?? false,
        symptomCheckinEnabled = preference?.SymptomCheckinEnabled ?? false,
        reminderTime = preference?.ReminderTime.ToString("HH:mm") ?? "09:00",
        periodReminderTime = preference?.PeriodReminderTime.ToString("HH:mm") ?? "09:00",
        contraceptiveReminderTime = preference?.ContraceptiveReminderTime.ToString("HH:mm") ?? "09:00",
        symptomCheckinTime = preference?.SymptomCheckinTime.ToString("HH:mm") ?? "09:00",
        timeZone = preference?.TimeZone ?? "America/Sao_Paulo"
    };
}

static bool IsValidNotificationTime(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        || NotificationPreferenceService.TryParseReminderTime(value, out _);
}

static string? NormalizePlan(string plan)
{
    var normalized = plan.Trim().ToLowerInvariant();
    return normalized is "basico" or "essencial" ? normalized : null;
}

public sealed record DevIncomingMessage(string From, string Body);
public sealed record AccountRegisterRequest(string Email, string Cpf, string FullName, string Password, string PhoneNumber, bool DataConsentAccepted);
public sealed record AccountLoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record PhoneVerificationConfirmRequest(string Code);
public sealed record PhoneChangeRequest(string PhoneNumber);
public sealed record PhoneChangeConfirmRequest(string PhoneNumber, string Code);
public sealed record CheckoutCreateSubscriptionRequest(string PlanCode, string? BillingInterval);
public sealed record CheckoutConfirmSubscriptionRequest(string PlanCode, string? BillingInterval, string StripeSubscriptionId, string? CardholderName, string? BillingCpf);
public sealed record ChangeSubscriptionPlanRequest(string PlanCode, string? BillingInterval);
public sealed record ConfirmPaymentMethodRequest(string SetupIntentId, string? CardholderName, string? BillingCpf);



