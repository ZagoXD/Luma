using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class ConversationServiceTests
{
    [Fact]
    public async Task Name_step_discards_unsafe_ai_age_inference()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(message =>
            message.Contains("Nayara", StringComparison.OrdinalIgnoreCase)
                ? new OnboardingExtraction { DisplayName = "Nayara", IsAdultConfirmed = false }
                : null));

        var phone = "+5516992000001";
        await SendAsync(service, phone, "Olá");
        await SendAsync(service, phone, "Aceito");
        var reply = await SendAsync(service, phone, "Pode me chamar de Nayara");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Equal("Nayara", user.DisplayName);
        Assert.Equal(OnboardingSteps.AwaitingAgeConfirmation, user.OnboardingStep);
        Assert.Null(user.IsAdultConfirmed);
        Assert.Contains("confirmar se tem 18 anos", reply);
    }

    [Fact]
    public async Task Name_step_does_not_save_unrecognized_sentence_as_display_name()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000002";
        await SendAsync(service, phone, "Olá");
        await SendAsync(service, phone, "Aceito");
        var reply = await SendAsync(service, phone, "meu ciclo costuma ter 29 dias");

        var user = await db.Users.SingleAsync(user => user.PhoneNumber == phone);
        Assert.Null(user.DisplayName);
        Assert.Equal(OnboardingSteps.AwaitingDisplayName, user.OnboardingStep);
        Assert.Contains("Não entendi sua resposta", reply);
    }

    [Fact]
    public async Task Last_period_step_saves_relative_date_and_event()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new FakeExtractor(_ => null));

        var phone = "+5516992000003";
        await SendAsync(service, phone, "Olá");
        await SendAsync(service, phone, "Aceito");
        await SendAsync(service, phone, "Nay");
        await SendAsync(service, phone, "Sim, tenho 23 anos");
        await SendAsync(service, phone, "começou há uns 5 dias");

        var user = await db.Users.Include(user => user.Preference).SingleAsync(user => user.PhoneNumber == phone);
        var periodStart = await db.CycleEvents.SingleAsync(ev => ev.UserId == user.Id && ev.Type == CycleEventTypes.PeriodStart);
        Assert.Equal(new DateOnly(2026, 4, 20), user.Preference!.LastPeriodStartDate);
        Assert.Equal(new DateOnly(2026, 4, 20), periodStart.Date);
    }

    private static LumaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LumaDbContext(options);
    }

    private static ConversationService CreateService(LumaDbContext db, IOnboardingDataExtractor extractor)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Luma:StoreMessageBodies"] = "false"
            })
            .Build();

        return new ConversationService(
            db,
            configuration,
            extractor,
            new FixedDateProvider(new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ConversationService>.Instance);
    }

    private static Task<string> SendAsync(ConversationService service, string phone, string body)
    {
        return service.HandleIncomingMessageAsync(new IncomingMessage("test", phone, body, null));
    }

    private sealed class FakeExtractor(Func<string, OnboardingExtraction?> extract) : IOnboardingDataExtractor
    {
        public Task<OnboardingExtraction?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(extract(message));
        }
    }

    private sealed class FixedDateProvider(DateTimeOffset utcNow) : IDateProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
