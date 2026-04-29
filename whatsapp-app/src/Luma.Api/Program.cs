using System.Text;
using System.Xml.Linq;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Stripe;
using LumaSubscriptionStatuses = Luma.Api.Models.SubscriptionStatuses;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.Configure<StripeBillingOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddHttpClient<OpenAiResponsesClient>();
builder.Services.AddScoped<IOnboardingDataExtractor, OpenAiOnboardingDataExtractor>();
builder.Services.AddScoped<IConversationIntentExtractor, OpenAiConversationIntentExtractor>();
builder.Services.AddScoped<ILumaToolAgent, OpenAiLumaToolAgent>();
builder.Services.AddScoped<ILumaResponseGenerator, OpenAiLumaResponseGenerator>();

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

app.MapPost("/account/register", async (AccountRegisterRequest request, LumaDbContext db) =>
{
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

    var exists = await db.AccountUsers.AnyAsync(user =>
        user.Email == normalized.Email || user.Cpf == normalized.Cpf || user.PhoneNumber == normalized.PhoneNumber);

    if (exists)
    {
        return Results.Conflict(new { message = "JÃ¡ existe uma conta com esse e-mail, CPF ou celular." });
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

    return Results.Ok(BuildAccountAuthResponse(account, builder.Configuration));
})
.WithName("RegisterAccount")
.WithOpenApi();

app.MapPost("/account/login", async (AccountLoginRequest request, LumaDbContext db) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var account = await db.AccountUsers.FirstOrDefaultAsync(user => user.Email == email);
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
    var lumaUser = await db.Users
        .AsNoTracking()
        .Include(user => user.Preference)
        .FirstOrDefaultAsync(user => user.PhoneNumber == account.PhoneNumber);

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

app.MapPost("/checkout/create-subscription", async (HttpRequest http, CheckoutCreateSubscriptionRequest request, LumaDbContext db, IConfiguration configuration) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var plan = NormalizePlan(request.PlanCode);
    if (plan is null)
    {
        return Results.BadRequest(new { message = "Plano inválido." });
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey) || string.IsNullOrWhiteSpace(stripeOptions.PublishableKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY e NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY para ativar pagamentos." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    account.StripeCustomerId = await EnsureStripeCustomerAsync(account);

    var now = DateTimeOffset.UtcNow;
    var previousPending = await db.AccountSubscriptions
        .Where(subscription => subscription.PhoneNumber == account.PhoneNumber
            && subscription.Status == LumaSubscriptionStatuses.Pending)
        .ToListAsync();

    foreach (var subscription in previousPending)
    {
        subscription.Status = LumaSubscriptionStatuses.Canceled;
        subscription.CanceledAt ??= now;
        subscription.UpdatedAt = now;
    }

    var stripeSubscription = await CreateStripeSubscriptionAsync(account.StripeCustomerId, plan, stripeOptions);
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
        Status = LumaSubscriptionStatuses.Pending,
        StripeSubscriptionId = stripeSubscription.Id,
        StartsAt = now,
        CurrentPeriodEndsAt = now.AddDays(30),
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

app.MapPost("/checkout/confirm-subscription", async (HttpRequest http, CheckoutConfirmSubscriptionRequest request, LumaDbContext db, IConfiguration configuration) =>
{
    var account = await GetAuthenticatedAccountAsync(http, db);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var plan = NormalizePlan(request.PlanCode);
    if (plan is null || string.IsNullOrWhiteSpace(request.StripeSubscriptionId))
    {
        return Results.BadRequest(new { message = "Assinatura inválida." });
    }

    var stripeOptions = GetStripeOptions(configuration);
    if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
    {
        return Results.BadRequest(new { message = "Configure STRIPE_SECRET_KEY para confirmar pagamentos." });
    }

    StripeConfiguration.ApiKey = stripeOptions.SecretKey;
    var stripeSubscription = await new SubscriptionService().GetAsync(request.StripeSubscriptionId);
    if (stripeSubscription.CustomerId != account.StripeCustomerId)
    {
        return Results.Unauthorized();
    }

    var localSubscription = await db.AccountSubscriptions
        .FirstOrDefaultAsync(subscription =>
            subscription.AccountUserId == account.Id
            && subscription.StripeSubscriptionId == request.StripeSubscriptionId);

    if (localSubscription is null)
    {
        return Results.NotFound(new { message = "Assinatura local não encontrada." });
    }

    localSubscription.Status = StripeStatusToLocalStatus(stripeSubscription.Status);
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

app.MapPost("/webhooks/twilio/whatsapp", async (HttpRequest request, ConversationService conversations) =>
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

    var reply = await conversations.HandleIncomingMessageAsync(new IncomingMessage(
        Provider: "twilio",
        From: PhoneNumber.Normalize(from),
        Body: body,
        ProviderMessageId: string.IsNullOrWhiteSpace(messageSid) ? null : messageSid));

    var twiml = new XDocument(
        new XElement("Response",
            new XElement("Message", reply)));

    return Results.Text(twiml.ToString(SaveOptions.DisableFormatting), "application/xml", Encoding.UTF8);
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
    "Email" character varying(180) NOT NULL,
    "Cpf" character varying(16) NOT NULL,
    "FullName" character varying(160) NOT NULL,
    "PasswordHash" character varying(512) NOT NULL,
    "PhoneNumber" character varying(64) NOT NULL,
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
CREATE TABLE IF NOT EXISTS account_subscriptions (
    "Id" uuid PRIMARY KEY,
    "AccountUserId" uuid NOT NULL REFERENCES account_users ("Id") ON DELETE CASCADE,
    "PhoneNumber" character varying(64) NOT NULL,
    "PlanCode" character varying(32) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "StripeSubscriptionId" character varying(128),
    "StartsAt" timestamp with time zone NOT NULL,
    "CurrentPeriodEndsAt" timestamp with time zone NOT NULL,
    "CanceledAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_account_subscriptions_PhoneNumber_Status_CurrentPeriodEndsAt" ON account_subscriptions ("PhoneNumber", "Status", "CurrentPeriodEndsAt");
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "StripeCustomerId" character varying(128);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "StripeSubscriptionId" character varying(128);
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
    return await db.AccountSubscriptions
        .Where(subscription => subscription.PhoneNumber == phoneNumber
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
        EssentialPriceId = configuration.GetValue<string>("Stripe:EssentialPriceId") ?? string.Empty
    };
}

static async Task<string> EnsureStripeCustomerAsync(AccountUser account)
{
    if (!string.IsNullOrWhiteSpace(account.StripeCustomerId))
    {
        return account.StripeCustomerId;
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

static async Task<Subscription> CreateStripeSubscriptionAsync(string customerId, string planCode, StripeBillingOptions options)
{
    var priceId = await ResolveStripePriceIdAsync(planCode, options);
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
            ["plan_code"] = planCode
        },
        Expand = ["latest_invoice.confirmation_secret", "items"]
    });
}

static async Task<string> ResolveStripePriceIdAsync(string planCode, StripeBillingOptions options)
{
    var configuredPrice = planCode == "basico" ? options.BasicPriceId : options.EssentialPriceId;
    if (!string.IsNullOrWhiteSpace(configuredPrice))
    {
        return configuredPrice;
    }

    var (name, amount) = planCode == "basico"
        ? ("Luma Básico", 590L)
        : ("Luma Essencial", 990L);

    var price = await new PriceService().CreateAsync(new PriceCreateOptions
    {
        Currency = "brl",
        UnitAmount = amount,
        Recurring = new PriceRecurringOptions
        {
            Interval = "month"
        },
        ProductData = new PriceProductDataOptions
        {
            Name = name
        }
    });

    return price.Id;
}

static DateTimeOffset? GetStripePeriodEnd(Subscription subscription)
{
    return subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
}

static string StripeStatusToLocalStatus(string stripeStatus)
{
    return stripeStatus is "active" or "trialing"
        ? LumaSubscriptionStatuses.Active
        : LumaSubscriptionStatuses.Pending;
}

static object BuildAccountAuthResponse(AccountUser account, IConfiguration configuration)
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
        user = BuildAccountUserResponse(account)
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
        subscription.Status,
        subscription.StripeSubscriptionId,
        subscription.StartsAt,
        subscription.CurrentPeriodEndsAt,
        subscription.CanceledAt
    };
}

static string? NormalizePlan(string plan)
{
    var normalized = plan.Trim().ToLowerInvariant();
    return normalized is "basico" or "essencial" ? normalized : null;
}

public sealed record DevIncomingMessage(string From, string Body);
public sealed record AccountRegisterRequest(string Email, string Cpf, string FullName, string Password, string PhoneNumber);
public sealed record AccountLoginRequest(string Email, string Password);
public sealed record CheckoutCreateSubscriptionRequest(string PlanCode);
public sealed record CheckoutConfirmSubscriptionRequest(string PlanCode, string StripeSubscriptionId);


