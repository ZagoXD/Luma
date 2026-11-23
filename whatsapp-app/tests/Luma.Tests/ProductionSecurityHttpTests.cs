using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Luma.Tests;

public sealed class ProductionSecurityHttpTests
{
    [PostgresFact]
    public async Task Signed_unsupported_media_and_group_webhooks_never_enter_the_conversation_or_call_paid_APIs()
    {
        await WithHost(async (host, client) =>
        {
            async Task<string> Send(Dictionary<string, string> fields)
            {
                fields["AccountSid"] = "ACoffline";
                fields["To"] = "whatsapp:+14155238886";
                fields["MessageSid"] = "SM" + Guid.NewGuid().ToString("N");
                var payload = "https://offline.test/webhooks/twilio/whatsapp" + string.Concat(fields.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + x.Value));
                using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/twilio/whatsapp") { Content = new FormUrlEncodedContent(fields) };
                request.Headers.Add("X-Twilio-Signature", Convert.ToBase64String(System.Security.Cryptography.HMACSHA1.HashData(System.Text.Encoding.UTF8.GetBytes("offline-token"), System.Text.Encoding.UTF8.GetBytes(payload))));
                var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return await response.Content.ReadAsStringAsync();
            }
            var types = new[] { "image/jpeg", "video/mp4", "application/pdf", "text/vcard" };
            for (var i = 0; i < types.Length; i++)
                Assert.Contains("não consigo", await Send(new() { ["From"] = "whatsapp:+551199999000" + i, ["NumMedia"] = "1", ["MediaContentType0"] = types[i], ["MediaUrl0"] = "https://never-fetch.invalid/file" }));
            Assert.Contains("anexo", await Send(new() { ["From"] = "whatsapp:+5511999990008", ["Body"] = "menstruei hoje", ["NumMedia"] = "1", ["MediaContentType0"] = "image/jpeg" }));
            Assert.Contains("não consigo", await Send(new() { ["From"] = "whatsapp:+5511999990009", ["Latitude"] = "-21", ["Longitude"] = "-47" }));
            var group = new Dictionary<string, string> { ["From"] = "whatsapp:12345@g.us", ["Body"] = "olá", ["GroupId"] = "private-group" };
            Assert.Contains("individuais", await Send(group));
            Assert.DoesNotContain("individuais", await Send(group));
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.UsageCalls.ToListAsync());
            Assert.Single(await db.BlockedConversations.ToListAsync());
        });
    }

    [PostgresFact]
    public async Task Usage_endpoint_requires_authentication_and_does_not_expose_another_accounts_usage()
    {
        await WithHost(async (host, client) =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/account/usage")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/dev/messages", new { from = "+5511999990001", body = "olá" })).StatusCode);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
            var account = new AccountUser { Email = "usage@example.invalid", Cpf = "52998224725", PhoneNumber = "+5511999990001" };
            db.AccountUsers.Add(account);
            await db.SaveChangesAsync();
            var session = await scope.ServiceProvider.GetRequiredService<AccountSessionService>().IssueAsync(account);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/account/usage")).StatusCode);
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                AccountUserId = account.Id,
                PhoneNumber = account.PhoneNumber,
                PlanCode = "basico",
                Status = SubscriptionStatuses.Canceled,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(30)
            });
            await db.SaveChangesAsync();
            var ledger = scope.ServiceProvider.GetRequiredService<UsageLedger>();
            await ledger.ReserveAsync(Guid.NewGuid(), "essencial", "other-account", "ai", 1);
            var result = await client.GetFromJsonAsync<JsonElement>("/account/usage");
            Assert.Equal(0, result.GetProperty("dailyUsed").GetInt32());
            Assert.Equal(10, result.GetProperty("dailyLimit").GetInt32());
            Assert.Equal(150, result.GetProperty("monthlyLimit").GetInt32());
            Assert.Equal(0, result.GetProperty("monthlyUsed").GetInt32());
            Assert.True(result.GetProperty("monthlyResetsAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        });
    }

    [PostgresFact]
    public async Task Monthly_usage_is_atomic_across_independent_database_connections()
    {
        await WithHost(async (host, client) =>
        {
            var policy = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<UsageOptions>>().Value;
            policy.BasicDailyMessages = 100;
            policy.BasicMonthlyMessages = 2;
            var account = Guid.NewGuid();
            var decisions = await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
            {
                using var scope = host.Services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<UsageLedger>().ReserveAsync(account, "basico", "monthly-race-" + index, "ai", 1);
            }));
            Assert.Equal(2, decisions.Count(decision => decision.Allowed));
            using var verify = host.Services.CreateScope();
            var snapshot = await verify.ServiceProvider.GetRequiredService<UsageLedger>().SnapshotAsync(account, "basico", default);
            Assert.Equal(2, snapshot.DailyUsed);
            Assert.Equal(2, snapshot.MonthlyUsed);
        });
    }
    [PostgresFact]
    public async Task Password_reset_link_is_consumed_once_even_under_concurrent_requests()
    {
        await WithHost(async (host, client) =>
        {
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
                db.AccountUsers.Add(new AccountUser { Email = "reset-race@example.com", Cpf = "52998224725", PhoneNumber = "+5511999990001", PasswordHash = "old" });
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/forgot-password", new { email = "reset-race@example.com" })).StatusCode);
            var token = Uri.UnescapeDataString(new Uri(host.Email.ResetLinks.Single()).Query.Split("token=")[1]);
            var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => client.PostAsJsonAsync("/auth/reset-password", new { token, newPassword = "new-password-2026-" + i })));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        });
    }

    [PostgresFact]
    public async Task Startup_upgrades_existing_security_schema_before_reading_accounts()
    {
        await WithHost(async (host, client) =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            using var scope = host.Services.CreateScope();
            var account = await scope.ServiceProvider.GetRequiredService<LumaDbContext>().AccountUsers.SingleAsync();
            Assert.Equal("legacy@example.com", account.Email);
            Assert.Equal(0, account.SessionVersion);
        }, legacySchema: true);
    }

    [PostgresFact]
    public async Task Registration_resend_login_reset_refresh_and_logout_are_guarded_without_paid_calls()
    {
        await WithHost(async (host, client) =>
        {
            var response = await client.PostAsJsonAsync("/account/register", new
            {
                email = "security@example.com",
                cpf = "52998224725",
                fullName = "Teste Segurança",
                password = "test-password-2026!",
                phoneNumber = "+5511999990001",
                dataConsentAccepted = true,
                termsAccepted = true,
                legalVersion = LegalConsent.Version
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
            var token = auth.GetProperty("token").GetString()!;
            var refresh = auth.GetProperty("refreshToken").GetString()!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account/me")).StatusCode);
            var resend = await client.PostAsJsonAsync("/account/phone-verification/send", new { });
            Assert.Equal((HttpStatusCode)429, resend.StatusCode);
            Assert.NotNull(resend.Headers.RetryAfter);
            Assert.Equal(1, host.Verify.Sends);
            for (var i = 0; i < 6; i++)
            {
                var bad = await client.PostAsJsonAsync("/account/login", new { email = "unknown@example.com", password = "incorrect-password" });
                Assert.Equal(i < 5 ? HttpStatusCode.Unauthorized : (HttpStatusCode)429, bad.StatusCode);
            }
            for (var i = 0; i < 5; i++)
            {
                var forgot = await client.PostAsJsonAsync("/auth/forgot-password", new { email = "security@example.com" });
                Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
            }
            Assert.Equal(3, host.Email.ResetLinks.Count);
            var resetToken = Uri.UnescapeDataString(new Uri(host.Email.ResetLinks.Last()).Query.Split("token=")[1]);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/reset-password", new { token = resetToken, newPassword = "changed-password-2026!" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/account/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/account/refresh", new { refreshToken = refresh })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/reset-password", new { token = resetToken, newPassword = "changed-again-2026!" })).StatusCode);
            var login = await client.PostAsJsonAsync("/account/login", new { email = "security@example.com", password = "changed-password-2026!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var current = await login.Content.ReadFromJsonAsync<JsonElement>();
            var rotatedResponse = await client.PostAsJsonAsync("/account/refresh", new { refreshToken = current.GetProperty("refreshToken").GetString() });
            Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
            var rotated = await rotatedResponse.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.GetProperty("token").GetString());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/account/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/account/logout", new { refreshToken = rotated.GetProperty("refreshToken").GetString() })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/account/me")).StatusCode);
            Assert.Equal(1, host.Verify.Sends);
        });
    }

    [PostgresFact]
    public async Task Notification_endpoint_preserves_paid_essential_benefits_without_renewal_and_returns_403_not_500()
    {
        await WithHost(async (host, client) =>
        {
            Guid accountId;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
                var account = new AccountUser { Email = "preferences@example.com", Cpf = "52998224725", PhoneNumber = "+5511999990001", PasswordHash = "test" };
                db.AccountUsers.Add(account);
                var user = new LumaUser { PhoneNumber = account.PhoneNumber, OnboardingStep = OnboardingSteps.Completed, ConsentAcceptedAt = DateTimeOffset.UtcNow };
                db.Users.Add(user);
                db.AccountSubscriptions.Add(new AccountSubscription
                {
                    AccountUserId = account.Id,
                    PhoneNumber = account.PhoneNumber,
                    PlanCode = "essencial",
                    Status = "active",
                    CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(5)
                });
                await db.SaveChangesAsync();
                accountId = account.Id;
                var tokens = await scope.ServiceProvider.GetRequiredService<AccountSessionService>().IssueAsync(account);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Token);
            }
            foreach (var sample in new[] { ("essencial","active",5,true), ("essencial","canceled",5,true),
                ("basico","active",5,false), ("essencial","terminated",5,false), ("essencial","pending",5,false), ("essencial","active",-1,false) })
            {
                using (var scope = host.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
                    var plan = await db.AccountSubscriptions.SingleAsync(s => s.AccountUserId == accountId);
                    plan.PlanCode = sample.Item1; plan.Status = sample.Item2; plan.CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(sample.Item3);
                    await db.SaveChangesAsync();
                }
                var saved = await client.PostAsJsonAsync("/account/notifications/preferences", new
                {
                    periodReminderEnabled = true,
                    periodReminderTime = "08:15",
                    contraceptiveReminderTime = "07:00",
                    symptomCheckinTime = "20:30",
                    timeZone = "America/Sao_Paulo"
                });
                Assert.Equal(sample.Item4 ? HttpStatusCode.OK : HttpStatusCode.Forbidden, saved.StatusCode);
            }
        });
    }

    [PostgresFact]
    public async Task Database_rate_limit_is_atomic_across_workers_and_survives_new_scopes()
    {
        await WithHost(async (host, _) =>
        {
            var calls = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
            {
                using var scope = host.Services.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IAccountRequestLimiter>().AllowAsync("concurrent", "private@example.com", 5, TimeSpan.FromMinutes(5));
            }));
            Assert.Equal(5, calls.Count(x => x));
            using var check = host.Services.CreateScope();
            Assert.False(await check.ServiceProvider.GetRequiredService<IAccountRequestLimiter>().AllowAsync("concurrent", "private@example.com", 5, TimeSpan.FromMinutes(5)));
            var rows = await check.ServiceProvider.GetRequiredService<LumaDbContext>().AuthRateLimits.ToListAsync();
            Assert.DoesNotContain(rows, x => x.Key.Contains("private"));
        });
    }

    [PostgresFact]
    public async Task Concurrent_refresh_returns_the_same_successor_without_logging_out_legitimate_requests()
    {
        await WithHost(async (host, client) =>
        {
            string refresh;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LumaDbContext>();
                var account = new AccountUser { Email = "rotation@example.com", Cpf = "52998224725", PhoneNumber = "+5511999990001", PasswordHash = "hash" };
                db.Add(account); await db.SaveChangesAsync();
                refresh = (await scope.ServiceProvider.GetRequiredService<AccountSessionService>().IssueAsync(account)).RefreshToken;
            }
            var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync("/account/refresh", new { refreshToken = refresh })));
            var successors = new List<string>();
            foreach (var response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); successors.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!); }
            Assert.Single(successors.Distinct());
        });
    }

    private static async Task WithHost(Func<TestHost, HttpClient, Task> run, bool legacySchema = false)
    {
        var original = Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")!;
        var schema = "http_security_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(original); await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(original) { SearchPath = schema };
            await using (var db = new LumaDbContext(new DbContextOptionsBuilder<LumaDbContext>().UseNpgsql(builder.ConnectionString).Options))
            {
                await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
                if (legacySchema)
                {
                    db.AccountUsers.Add(new AccountUser { Email = "legacy@example.com", Cpf = "52998224725", PhoneNumber = "+5511999990001", PasswordHash = "test" });
                    await db.SaveChangesAsync();
                    await db.Database.ExecuteSqlRawAsync("""
                        ALTER TABLE account_users DROP COLUMN "SessionVersion";
                        ALTER TABLE account_sessions DROP COLUMN "AccountVersion", DROP COLUMN "RefreshGeneration", DROP COLUMN "PreviousValidUntil", DROP COLUMN "RevokedAt";
                        ALTER TABLE password_reset_tokens DROP COLUMN "AccountVersion";
                        DROP TABLE auth_rate_limits;
                        """);
                }
            }
            await using var host = new TestHost(builder.ConnectionString);
            using var client = host.CreateClient();
            await run(host, client);
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection); await drop.ExecuteNonQueryAsync(); }
    }

    private sealed class TestHost(string database) : WebApplicationFactory<Program>
    {
        public readonly VerifySpy Verify = new();
        public readonly EmailSpy Email = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").UseSetting("ConnectionStrings:DefaultConnection", database)
                .UseSetting("Luma:JwtSigningKey", new string('s', 64)).UseSetting("Privacy:EncryptionEnabled", "false")
                .UseSetting("Rag:Enabled", "false").UseSetting("Notifications:WorkerEnabled", "false")
                .UseSetting("Billing:ReconciliationEnabled", "false").UseSetting("Twilio:VerifyDevelopmentMode", "false");
            builder.UseSetting("Twilio:WebhookUrl", "https://offline.test/webhooks/twilio/whatsapp")
                .UseSetting("Twilio:AuthToken", "offline-token").UseSetting("Twilio:AccountSid", "ACoffline")
                .UseSetting("Twilio:WhatsAppFrom", "whatsapp:+14155238886").UseSetting("Twilio:TypingIndicatorsEnabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITwilioVerifyClient>(); services.AddSingleton<ITwilioVerifyClient>(Verify);
                services.RemoveAll<IEmailService>(); services.AddSingleton<IEmailService>(Email);
                services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory, NoNetworkFactory>();
            });
        }
    }
    private sealed class NoNetworkFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(new DenyNetwork()); }
    private sealed class DenyNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("External HTTP is forbidden in security tests.");
    }
    private sealed class VerifySpy : ITwilioVerifyClient
    {
        public int Sends;
        public Task<TwilioVerifySendResult> SendVerificationAsync(string to, CancellationToken ct = default) { Interlocked.Increment(ref Sends); return Task.FromResult(new TwilioVerifySendResult(true, null, "pending", null)); }
        public Task<TwilioVerifyCheckResult> CheckVerificationAsync(string to, string code, CancellationToken ct = default) => Task.FromResult(new TwilioVerifyCheckResult(code == "123456", "approved", null));
    }
    private sealed class EmailSpy : IEmailService
    {
        public List<string> ResetLinks { get; } = [];
        private static Task<EmailSendResult> Ok() => Task.FromResult(new EmailSendResult(true, "fake", null));
        public Task<EmailSendResult> SendWelcomeEmailAsync(string to, string? name, CancellationToken ct = default) => Ok();
        public Task<EmailSendResult> SendSubscriptionCreatedEmailAsync(string to, string? name, string plan, CancellationToken ct = default) => Ok();
        public Task<EmailSendResult> SendPasswordResetEmailAsync(string to, string? name, string url, int minutes, CancellationToken ct = default) { ResetLinks.Add(url); return Ok(); }
        public Task<EmailSendResult> SendSupportRequestToAdminAsync(SupportRequest request, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct = default) => Ok();
        public Task<EmailSendResult> SendSupportRequestConfirmationToUserAsync(SupportRequest request, CancellationToken ct = default) => Ok();
    }
}
