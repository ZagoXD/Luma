using System.Text;
using System.Xml.Linq;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<LumaDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=luma;Username=luma;Password=luma_dev_password";

    options.UseNpgsql(connectionString);
});
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<IOnboardingDataExtractor, OnboardingAiExtractor>();
builder.Services.AddHttpClient<IConversationIntentExtractor, OllamaConversationIntentExtractor>();
builder.Services.AddHttpClient<ILumaToolAgent, OllamaLumaToolAgent>();
builder.Services.AddHttpClient<ILumaResponseGenerator, OllamaLumaResponseGenerator>();
builder.Services.AddSingleton<IDateProvider, SystemDateProvider>();
builder.Services.AddScoped<ConversationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
""");
}

public sealed record DevIncomingMessage(string From, string Body);
