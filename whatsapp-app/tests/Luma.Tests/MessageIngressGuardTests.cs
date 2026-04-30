using Luma.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class MessageIngressGuardTests
{
    [Fact]
    public async Task BeginAsync_RateLimitsAfterConfiguredWindowLimit()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Luma:RateLimit:WindowSeconds"] = "60",
                ["Luma:RateLimit:MaxMessages"] = "2",
                ["Luma:RateLimit:CooldownSeconds"] = "60",
                ["Luma:MessageLockSeconds"] = "10",
                ["Luma:DeduplicationSeconds"] = "60"
            })
            .Build();
        var redis = new RedisConnectionProvider(configuration, NullLogger<RedisConnectionProvider>.Instance);
        var guard = new MessageIngressGuard(redis, memory, configuration, NullLogger<MessageIngressGuard>.Instance);

        await using ((await guard.BeginAsync("twilio", "+5516992330309", "SM1")).Lease)
        {
        }

        await using ((await guard.BeginAsync("twilio", "+5516992330309", "SM2")).Lease)
        {
        }

        var blocked = await guard.BeginAsync("twilio", "+5516992330309", "SM3");

        Assert.False(blocked.AllowProcessing);
        Assert.Contains("muitas mensagens", blocked.Reply);
    }

    [Fact]
    public async Task BeginAsync_DeduplicatesProviderMessageId()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder().Build();
        var redis = new RedisConnectionProvider(configuration, NullLogger<RedisConnectionProvider>.Instance);
        var guard = new MessageIngressGuard(redis, memory, configuration, NullLogger<MessageIngressGuard>.Instance);

        await using ((await guard.BeginAsync("twilio", "+5516992330309", "SM1")).Lease)
        {
        }

        var duplicate = await guard.BeginAsync("twilio", "+5516992330309", "SM1");

        Assert.False(duplicate.AllowProcessing);
        Assert.Null(duplicate.Reply);
    }
}
