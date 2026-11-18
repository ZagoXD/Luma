using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public static class DatabaseStartupLock
{
    // Session-scoped lock shared by every API instance using this database.
    private const long LockId = 7612943025481;

    public static async Task<IAsyncDisposable> AcquireAsync(LumaDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({LockId})");
            return new Lease(db);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    private sealed class Lease(LumaDbContext db) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({LockId})");
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
