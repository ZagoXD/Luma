using System.Security.Cryptography;
using System.Text;
using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class UsageOptions
{
    public int BasicDailyMessages { get; set; } = 10;
    public int EssentialDailyMessages { get; set; } = 20;
    public int BasicMonthlyMessages { get; set; } = 150;
    public int EssentialMonthlyMessages { get; set; } = 300;
    public int OnboardingDailyMessages { get; set; } = 12;
    public int MonthlyAudioSeconds { get; set; } = 600;
    public int MaxAudioSeconds { get; set; } = 60;
    public int MonthlyImages { get; set; } = 2;
    public string TimeZone { get; set; } = "America/Sao_Paulo";
    public int DailyLimit(string plan) => plan == "essencial" ? EssentialDailyMessages : BasicDailyMessages;
    public int MonthlyLimit(string plan) => plan == "essencial" ? EssentialMonthlyMessages : BasicMonthlyMessages;
}

public sealed class UsageBucket
{
    public string Key { get; set; } = "";
    public Guid AccountId { get; set; }
    public string Kind { get; set; } = "";
    public long Units { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class UsageReceipt
{
    public string Id { get; set; } = "";
    public Guid AccountId { get; set; }
    public string Kind { get; set; } = "";
    public int Units { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UsageCall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "reserved";
    public int InputTokens { get; set; }
    public int CachedTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedUsd { get; set; }
    public decimal? ReferenceCostUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record UsageDecision(bool Allowed, bool Duplicate = false, string? Message = null);
public sealed record UsageSnapshot(int DailyLimit, long DailyUsed, int MonthlyAudioSeconds,
    long AudioSecondsUsed, int MonthlyImages, long ImagesUsed, string TimeZone,
    int MonthlyLimit, long MonthlyUsed, DateTimeOffset DailyResetsAt, DateTimeOffset MonthlyResetsAt);

public sealed class UsageContext
{
    public Guid? AccountId { get; set; }
    public string Plan { get; set; } = "basico";
    public string MessageId { get; set; } = "";
    public bool Admitted { get; set; }
}

// Usage commits independently: rolling back a health record cannot undo provider costs.
public sealed class UsageLedger(DbContextOptions<LumaDbContext> options, IOptions<UsageOptions> settings, TimeProvider clock)
{
    private static readonly SemaphoreSlim LocalGate = new(1, 1);
    public UsageOptions Policy => settings.Value;

    public Task<UsageDecision> ReserveAsync(Guid account, string plan, string message, string kind, int units, CancellationToken ct = default)
        => ReserveManyAsync(account, plan, message, [(kind, units)], ct);

    public Task<UsageDecision> ReserveAudioAsync(Guid account, string plan, string message, int seconds, CancellationToken ct = default)
        => ReserveManyAsync(account, plan, message, [("ai", 1), ("audio", seconds)], ct);

    private async Task<UsageDecision> ReserveManyAsync(Guid account, string plan, string message, (string Kind, int Units)[] requests, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(Policy.TimeZone));
        requests = requests.SelectMany(item => item.Kind is "ai" or "onboarding" ? new[] { item, ("ai_month", item.Units) } : new[] { item }).ToArray();
        await using var db = new LumaDbContext(options);
        var localOnly = !db.Database.IsNpgsql();
        if (localOnly) await LocalGate.WaitAsync(ct);
        try
        {
            await using var tx = localOnly ? null : await db.Database.BeginTransactionAsync(ct);
            if (tx is not null)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({account.ToString()}, 18))", ct);
            foreach (var (kind, units) in requests)
            {
                var limit = Limit(plan, kind);
                if (account == Guid.Empty || string.IsNullOrWhiteSpace(message) || units <= 0 || limit <= 0 || kind == "audio" && units > Policy.MaxAudioSeconds)
                    return new(false, Message: "Este recurso não está disponível ou excede o limite por envio.");
                var receipt = ReceiptId(account, kind, message);
                if (await db.UsageReceipts.AnyAsync(x => x.Id == receipt, ct)) return new(true, true);
                var key = $"{account:N}:{kind}:{local.ToString(kind is "audio" or "image" or "ai_month" ? "yyyy-MM" : "yyyy-MM-dd")}";
                var row = await db.UsageBuckets.SingleOrDefaultAsync(x => x.Key == key, ct);
                var used = row?.Units ?? (kind == "ai_month" ? await LegacyMonthlyCountAsync(db, account, local, ct) : 0);
                if (used + units > limit) return new(false, Message: LimitMessage(kind));
                if (row is null)
                {
                    row = new() { Key = key, AccountId = account, Kind = kind, Units = used, ExpiresAt = now.AddDays(40) };
                    db.UsageBuckets.Add(row);
                }
                row.Units += units;
                db.UsageReceipts.Add(new() { Id = receipt, AccountId = account, Kind = kind, Units = units, CreatedAt = now });
            }
            // A rejected audio request never spends just one of its two allowances.
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return new(true);
        }
        finally { if (localOnly) LocalGate.Release(); }
    }

    private int Limit(string plan, string kind) => kind switch
    {
        "ai" => Policy.DailyLimit(plan),
        "ai_month" => Policy.MonthlyLimit(plan),
        "onboarding" => Policy.OnboardingDailyMessages,
        "audio" => plan == "essencial" ? Policy.MonthlyAudioSeconds : 0,
        "image" => plan == "essencial" ? Policy.MonthlyImages : 0,
        _ => 0
    };

    private static string ReceiptId(Guid account, string kind, string message)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{account:N}:{kind}:{message}")));

    private static string LimitMessage(string kind) => kind switch
    {
        "ai" or "onboarding" => "Você chegou ao limite de conversas com IA de hoje. Seus registros continuam disponíveis: envie *registrar* para usar o menu sem IA. As conversas são liberadas novamente amanhã.",
        "ai_month" => "Você chegou ao limite de conversas com IA deste mês. A cota renova no primeiro dia do próximo mês, à meia-noite de São Paulo. Envie *registrar* para continuar seus registros sem IA, *meu calendário* para abrir o painel ou *meu uso* para consultar os limites.",
        "audio" => "Os minutos de áudio deste mês terminaram. Você ainda pode escrever ou usar o menu *registrar*.",
        _ => "Você atingiu o limite de imagens deste mês. Seus registros e consultas continuam disponíveis."
    };

    public async Task<Guid?> StartCallAsync(UsageContext? context, string provider, string model, decimal estimate, CancellationToken ct)
    {
        if (context?.AccountId is not Guid account) return null;
        await using var db = new LumaDbContext(options);
        var row = new UsageCall { AccountId = account, Provider = provider, Model = model, EstimatedUsd = estimate, CreatedAt = clock.GetUtcNow() };
        db.UsageCalls.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task CompleteCallAsync(Guid? id, int input, int cached, int output, decimal? referenceCost, CancellationToken ct)
    {
        if (id is null) return;
        await using var db = new LumaDbContext(options);
        var row = await db.UsageCalls.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return; // The account may have been deleted while the provider was processing.
        row.InputTokens = input;
        row.CachedTokens = cached;
        row.OutputTokens = output;
        row.ReferenceCostUsd = referenceCost;
        row.Status = "completed";
        await db.SaveChangesAsync(ct);
    }

    public async Task<UsageSnapshot> SnapshotAsync(Guid account, string plan, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(Policy.TimeZone));
        await using var db = new LumaDbContext(options);
        async Task<long> Count(string kind, string period)
        {
            var key = $"{account:N}:{kind}:{period}";
            return await db.UsageBuckets.Where(x => x.Key == key).Select(x => (long?)x.Units).SingleOrDefaultAsync(ct) ?? 0;
        }
        var monthKey = $"{account:N}:ai_month:{local:yyyy-MM}";
        var monthly = await db.UsageBuckets.Where(x => x.Key == monthKey).Select(x => (long?)x.Units).SingleOrDefaultAsync(ct)
            ?? await LegacyMonthlyCountAsync(db, account, local, ct);
        return new(Policy.DailyLimit(plan), await Count("ai", local.ToString("yyyy-MM-dd")),
            plan == "essencial" ? Policy.MonthlyAudioSeconds : 0, await Count("audio", local.ToString("yyyy-MM")),
            plan == "essencial" ? Policy.MonthlyImages : 0, await Count("image", local.ToString("yyyy-MM")), Policy.TimeZone,
            Policy.MonthlyLimit(plan), monthly, LocalMidnight(local.Date.AddDays(1)), LocalMidnight(new DateTime(local.Year, local.Month, 1).AddMonths(1)));
    }

    private DateTimeOffset LocalMidnight(DateTime value) => new(TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById(Policy.TimeZone)));

    private async Task<long> LegacyMonthlyCountAsync(LumaDbContext db, Guid account, DateTimeOffset local, CancellationToken ct)
    {
        var start = LocalMidnight(new DateTime(local.Year, local.Month, 1));
        var end = LocalMidnight(new DateTime(local.Year, local.Month, 1).AddMonths(1));
        return await db.UsageReceipts.Where(x => x.AccountId == account && (x.Kind == "ai" || x.Kind == "onboarding") && x.CreatedAt >= start && x.CreatedAt < end)
            .SumAsync(x => (long?)x.Units, ct) ?? 0;
    }
}
