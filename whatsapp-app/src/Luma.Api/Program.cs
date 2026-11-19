using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Luma.Api.Services.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Luma.Api.Billing;
using LumaSubscriptionStatuses = Luma.Api.Models.SubscriptionStatuses;

if (args.Length == 2 && args[0] == "--knowledge-release")
{
    var documents = KnowledgeCorpus.Validate(JsonSerializer.Deserialize<KnowledgeDocument[]>(
        await File.ReadAllTextAsync(args[1]), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []);
    Console.WriteLine(KnowledgeCorpus.Version(documents));
    return;
}

if (args.Length == 2 && args[0] == "--inspect-knowledge")
{
    using var pdf = UglyToad.PdfPig.PdfDocument.Open(args[1]);
    foreach (var page in pdf.GetPages())
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { page = page.Number,
            text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page) }));
    return;
}

if (args.Length == 4 && args[0] == "--ingest-knowledge")
{
    await KnowledgeIngestor.IngestAsync(args[1], args[2], args[3]);
    return;
}

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
builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection("Assistant"));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("Rag"));
builder.Services.AddHttpClient<IAssistantModel, OpenAiAssistantModel>(http => http.Timeout = TimeSpan.FromSeconds(40));
builder.Services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>(http => http.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<AssistantOrchestrator>();
builder.Services.AddScoped<KnowledgeStore>();
builder.Services.Configure<R2Options>(builder.Configuration.GetSection("R2"));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.Configure<ElevenLabsOptions>(builder.Configuration.GetSection("ElevenLabs"));
builder.Services.Configure<MercadoPagoOptions>(builder.Configuration.GetSection("MercadoPago"));
builder.Services.AddHttpClient<ISubscriptionGateway, MercadoPagoGateway>(http => http.Timeout = TimeSpan.FromSeconds(25));
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddScoped<IUserImageDeletion, R2UserDataDeletion>();
builder.Services.AddHostedService<BillingReconciliationWorker>();
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
if (PhoneVerificationPolicy.UseDevelopmentClient(builder.Configuration))
    builder.Services.AddSingleton<ITwilioVerifyClient, DevelopmentVerifyClient>();
else
    builder.Services.AddHttpClient<ITwilioVerifyClient, TwilioVerifyClient>();
builder.Services.AddSingleton<BabyImageJobQueue>();
builder.Services.AddSingleton<IBabyImageJobQueue>(provider => provider.GetRequiredService<BabyImageJobQueue>());
builder.Services.AddScoped<AccountPhoneVerificationService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<SupportRequestService>();
builder.Services.AddScoped<NotificationPreferenceService>();
builder.Services.AddScoped<NotificationProcessor>();
builder.Services.AddScoped<NotificationStatusService>();
builder.Services.AddScoped<CycleCalendarService>();
builder.Services.AddHostedService<NotificationWorker>();
NotificationConfiguration.Validate(builder.Configuration);
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
    await using var startupLock = await DatabaseStartupLock.AcquireAsync(db);
    await db.Database.EnsureCreatedAsync();
    await EnsureRuntimeSchemaAsync(db);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS assistant_turns (
            "Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
            "Provider" text NOT NULL, "MessageId" text NOT NULL, "Request" text NOT NULL,
            "Reply" text NOT NULL, "MediaUrl" text, "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_assistant_receipt ON assistant_turns ("Provider","MessageId");
        CREATE INDEX IF NOT EXISTS ix_assistant_memory ON assistant_turns ("UserId","CreatedAt");
        """);
    await scope.ServiceProvider.GetRequiredService<KnowledgeStore>().PublishAsync();
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
    if (LegalConsent.Validate(request.TermsAccepted, request.LegalVersion) is { } legalError)
        return Results.BadRequest(new { message = legalError });

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
        PasswordHash = AccountSecurity.HashPassword(request.Password),
        TermsVersion = LegalConsent.Version, PrivacyVersion = LegalConsent.Version,
        TermsAcceptedAt = DateTimeOffset.UtcNow, HealthConsentAcceptedAt = DateTimeOffset.UtcNow
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
    if (account is null || account.DeletionRequestedAt != null || !AccountSecurity.VerifyPassword(request.Password, account.PasswordHash))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(BuildAccountAuthResponse(account, builder.Configuration));
})
.WithName("LoginAccount")
.WithOpenApi();

app.MapGet("/account/me", async (HttpRequest request, LumaDbContext db, ISubscriptionGateway gateway) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var subscription = await GetVisibleSubscriptionAsync(db, account.PhoneNumber);
    var pendingCheckout = await db.AccountSubscriptions.AsNoTracking()
        .Where(s => s.AccountUserId == account.Id && s.BillingProvider == gateway.Provider && s.Status == LumaSubscriptionStatuses.Pending && s.PaymentAttemptedAt != null)
        .OrderByDescending(s => s.CreatedAt).Select(s => new { s.Id, s.PlanCode, s.BillingInterval, s.StartsAt, s.ReplacesSubscriptionId, scheduled = s.ReplacementConfirmedAt != null }).FirstOrDefaultAsync();
    var userPhoneHash = PrivacyRuntime.LookupHash(account.PhoneNumber, "user.phone");
    var lumaUser = await db.Users
        .AsNoTracking()
        .Include(user => user.Preference)
        .FirstOrDefaultAsync(user => user.PhoneHash == userPhoneHash);

    return Results.Ok(new
    {
        user = BuildAccountUserResponse(account),
        subscription = subscription is null ? null : BuildSubscriptionResponse(subscription),
        pendingCheckout,
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

app.MapBilling(GetAuthenticatedAccountAsync);
app.MapPost("/account/delete", async (HttpRequest request, DeleteAccountRequest input, LumaDbContext db, AccountDeletionService deletion, CancellationToken ct) =>
{
    var account = await GetAuthenticatedAccountAsync(request, db);
    if (account is null) return Results.Unauthorized();
    try { await deletion.RequestAsync(account.Id, input.Password, ct); return Results.Ok(new { message = "Conta excluída. As cobranças foram interrompidas. Backups expiram conforme a política de privacidade." }); }
    catch (BillingException ex) { return account.DeletionRequestedAt is not null
        ? Results.Json(new { pending = true, message = "Exclusão registrada. O acesso foi bloqueado. A interrupção externa das cobranças aguarda confirmação; contate o suporte para acompanhar." }, statusCode: 202)
        : Results.BadRequest(new { message = ex.Message }); }
    catch (HttpRequestException) { return Results.Json(new { message = "Exclusão registrada. Seu acesso foi bloqueado; a interrupção das cobranças será tentada novamente. Contate o suporte para acompanhar." }, statusCode: 202); }
});

app.MapPost("/webhooks/twilio/notifications/status/{deliveryId:guid}", async (
    Guid deliveryId, HttpRequest request, NotificationStatusService statuses, IConfiguration configuration) =>
{
    if (!request.HasFormContentType) return Results.BadRequest();
    var callbackBase = configuration["Twilio:NotificationStatusCallbackBaseUrl"];
    var token = configuration["Twilio:AuthToken"];
    var account = configuration["Twilio:AccountSid"];
    var sender = configuration["Twilio:WhatsAppFrom"];
    if (string.IsNullOrWhiteSpace(callbackBase) || string.IsNullOrWhiteSpace(token)
        || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(sender)) return Results.StatusCode(503);
    var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
    if (!TwilioWebhookAuthentication.IsValidNotificationStatus($"{callbackBase.TrimEnd('/')}/{deliveryId:D}", token,
        account, sender, form, request.Headers["X-Twilio-Signature"].ToString(), request.QueryString.HasValue)) return Results.StatusCode(403);
    await statuses.ApplyAsync(deliveryId, form["MessageSid"].ToString(), form["MessageStatus"].ToString(),
        form["ErrorCode"].ToString(), request.HttpContext.RequestAborted);
    return Results.NoContent();
}).DisableAntiforgery();

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

    var webhookUrl = configuration["Twilio:WebhookUrl"];
    var authToken = configuration["Twilio:AuthToken"];
    var accountSid = configuration["Twilio:AccountSid"];
    var recipient = configuration["Twilio:WhatsAppFrom"];
    if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(authToken)
        || string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(recipient))
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
    if (!TwilioWebhookAuthentication.IsValid(webhookUrl, authToken, accountSid, recipient, form,
        request.Headers["X-Twilio-Signature"].ToString(), request.QueryString.HasValue))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

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
    "BillingCustomerId" character varying(128),
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
    "ProviderSubscriptionId" character varying(128),
    "ProviderPlanId" character varying(128),
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
ALTER TABLE notification_deliveries ADD COLUMN IF NOT EXISTS "Attempts" integer NOT NULL DEFAULT 0;
ALTER TABLE notification_deliveries ADD COLUMN IF NOT EXISTS "NextAttemptAt" timestamp with time zone;
CREATE TABLE IF NOT EXISTS blocked_conversations (
    "Id" uuid PRIMARY KEY,
    "Provider" character varying(32) NOT NULL,
    "From" text NOT NULL,
    "FromHash" character varying(128) NOT NULL DEFAULT '',
    "Reason" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_blocked_conversations_Provider_CreatedAt" ON blocked_conversations ("Provider", "CreatedAt");
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "BillingCustomerId" character varying(128);
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
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "ProviderSubscriptionId" character varying(128);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "ProviderPlanId" character varying(128);
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

    await BillingSchema.EnsureAsync(db);
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

    return await db.AccountUsers.FirstOrDefaultAsync(account => account.Id == accountId.Value && account.DeletionRequestedAt == null);
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

static object BuildSubscriptionResponse(AccountSubscription subscription) => BillingEndpoints.View(subscription);

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

public sealed record DevIncomingMessage(string From, string Body);
public sealed record AccountRegisterRequest(string Email, string Cpf, string FullName, string Password, string PhoneNumber, bool DataConsentAccepted, bool TermsAccepted = false, string? LegalVersion = null);
public sealed record DeleteAccountRequest(string Password);
public sealed record AccountLoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record PhoneVerificationConfirmRequest(string Code);
public sealed record PhoneChangeRequest(string PhoneNumber);
public sealed record PhoneChangeConfirmRequest(string PhoneNumber, string Code);



