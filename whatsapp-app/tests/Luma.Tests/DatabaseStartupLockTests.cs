using Luma.Api.Data;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Luma.Tests;

public sealed class DatabaseStartupLockTests
{
    [PostgresFact]
    public async Task Concurrent_instances_wait_until_schema_initialization_finishes()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE"))
            .Options;
        await using var firstDb = new LumaDbContext(options);
        await using var secondDb = new LumaDbContext(options);
        var first = await DatabaseStartupLock.AcquireAsync(firstDb);
        Task<IAsyncDisposable>? waiting = null;
        try
        {
            waiting = DatabaseStartupLock.AcquireAsync(secondDb);
            Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(150)));
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using var second = await waiting!.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
