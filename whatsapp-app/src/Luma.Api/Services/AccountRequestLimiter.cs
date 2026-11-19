using System.Data;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Luma.Api.Services;

public sealed class AuthRateLimitException(int retryAfterSeconds) : Exception("Muitas tentativas. Aguarde um pouco antes de tentar novamente.")
{
    public int RetryAfterSeconds { get; } = Math.Max(1, retryAfterSeconds);
}

public interface IAccountRequestLimiter
{
    Task<bool> AllowAsync(string scope, string subject, int limit, TimeSpan window, CancellationToken ct = default);
}

public sealed class AccountRequestLimiter(LumaDbContext db, TimeProvider? clock = null) : IAccountRequestLimiter
{
    private static readonly SemaphoreSlim TestGate = new(1);
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<bool> AllowAsync(string scope, string subject, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var key = PrivacyRuntime.LookupHash(scope + ":" + subject, "auth.rate-limit");
        var now = time.GetUtcNow();
        if (!db.Database.IsNpgsql())
        {
            await TestGate.WaitAsync(ct);
            try
            {
                var row = await db.AuthRateLimits.SingleOrDefaultAsync(x => x.Key == key, ct);
                if (row is null) { row = new AuthRateLimit { Key = key }; db.AuthRateLimits.Add(row); }
                if (row.ExpiresAt <= now) { row.Attempts = 0; row.ExpiresAt = now + window; }
                row.Attempts++;
                await db.SaveChangesAsync(ct);
                return row.Attempts <= limit;
            }
            finally { TestGate.Release(); }
        }
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO auth_rate_limits ("Key","Attempts","ExpiresAt") VALUES (@key,1,@expires)
                ON CONFLICT ("Key") DO UPDATE SET
                    "Attempts" = CASE WHEN auth_rate_limits."ExpiresAt" <= @now THEN 1 ELSE LEAST(auth_rate_limits."Attempts" + 1, 1000000) END,
                    "ExpiresAt" = CASE WHEN auth_rate_limits."ExpiresAt" <= @now THEN @expires ELSE auth_rate_limits."ExpiresAt" END
                RETURNING "Attempts";
                """;
            Add("key", key); Add("expires", now + window); Add("now", now);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            command.Parameters.Clear();
            command.CommandText = "DELETE FROM auth_rate_limits WHERE \"ExpiresAt\" < @cutoff";
            Add("cutoff", now.AddDays(-1));
            await command.ExecuteNonQueryAsync(ct);
            return count <= limit;
            void Add(string name, object value) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
        }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }
}

public sealed class AuthRateLimit
{
    public string Key { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
