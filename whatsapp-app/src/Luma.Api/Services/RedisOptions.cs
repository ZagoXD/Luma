using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace Luma.Api.Services;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class MessageIngressOptions
{
    public int WindowSeconds { get; set; } = 30;
    public int MaxMessages { get; set; } = 5;
    public int CooldownSeconds { get; set; } = 60;
    public int MessageLockSeconds { get; set; } = 25;
    public int DeduplicationSeconds { get; set; } = 300;
}

public sealed class RedisConnectionProvider(IConfiguration configuration, ILogger<RedisConnectionProvider> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnectionMultiplexer? _connection;
    private bool _attempted;

    public async Task<IConnectionMultiplexer?> GetConnectionAsync()
    {
        if (_attempted)
        {
            return _connection;
        }

        await _gate.WaitAsync();
        try
        {
            if (_attempted)
            {
                return _connection;
            }

            _attempted = true;
            var connectionString = configuration.GetValue<string>("Redis:ConnectionString");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.LogInformation("Redis connection string not configured. Falling back to in-memory ingress controls.");
                return null;
            }

            _connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
            logger.LogInformation("Connected to Redis for Luma ingress controls.");
            return _connection;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to Redis. Falling back to in-memory ingress controls.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}

public sealed record MessageIngressDecision(bool AllowProcessing, string? Reply, IAsyncDisposable? Lease = null)
{
    public static MessageIngressDecision Allow(IAsyncDisposable? lease) => new(true, null, lease);
    public static MessageIngressDecision Block(string? reply = null) => new(false, reply);
}

public sealed class MessageIngressGuard(
    RedisConnectionProvider redis,
    IMemoryCache memory,
    IConfiguration configuration,
    ILogger<MessageIngressGuard> logger)
{
    private readonly MessageIngressOptions _options = new()
    {
        WindowSeconds = configuration.GetValue("Luma:RateLimit:WindowSeconds", 30),
        MaxMessages = configuration.GetValue("Luma:RateLimit:MaxMessages", 5),
        CooldownSeconds = configuration.GetValue("Luma:RateLimit:CooldownSeconds", 60),
        MessageLockSeconds = configuration.GetValue("Luma:MessageLockSeconds", 25),
        DeduplicationSeconds = configuration.GetValue("Luma:DeduplicationSeconds", 300)
    };

    public async Task<MessageIngressDecision> BeginAsync(string provider, string phone, string? providerMessageId)
    {
        var connection = await redis.GetConnectionAsync();
        try
        {
            return connection is null
                ? BeginWithMemory(provider, phone, providerMessageId)
                : await BeginWithRedisAsync(connection.GetDatabase(), provider, phone, providerMessageId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ingress guard failed. Allowing message without Redis controls.");
            return MessageIngressDecision.Allow(null);
        }
    }

    private async Task<MessageIngressDecision> BeginWithRedisAsync(IDatabase db, string provider, string phone, string? providerMessageId)
    {
        if (!string.IsNullOrWhiteSpace(providerMessageId))
        {
            var dedupeKey = $"luma:dedupe:{provider}:{providerMessageId}";
            if (!await db.StringSetAsync(dedupeKey, "1", TimeSpan.FromSeconds(_options.DeduplicationSeconds), When.NotExists))
            {
                return MessageIngressDecision.Block();
            }
        }

        var cooldownKey = $"luma:cooldown:{phone}";
        if (await db.KeyExistsAsync(cooldownKey))
        {
            var notifiedKey = $"luma:cooldown-notified:{phone}";
            var shouldNotify = await db.StringSetAsync(notifiedKey, "1", TimeSpan.FromSeconds(_options.CooldownSeconds), When.NotExists);
            return MessageIngressDecision.Block(shouldNotify ? RateLimitReply() : null);
        }

        var counterKey = $"luma:rate:{phone}:messages";
        var count = await db.StringIncrementAsync(counterKey);
        if (count == 1)
        {
            await db.KeyExpireAsync(counterKey, TimeSpan.FromSeconds(_options.WindowSeconds));
        }

        if (count > _options.MaxMessages)
        {
            await db.StringSetAsync(cooldownKey, "1", TimeSpan.FromSeconds(_options.CooldownSeconds));
            return MessageIngressDecision.Block(RateLimitReply());
        }

        var lockKey = $"luma:lock:{phone}";
        var lockValue = Guid.NewGuid().ToString("N");
        if (!await db.StringSetAsync(lockKey, lockValue, TimeSpan.FromSeconds(_options.MessageLockSeconds), When.NotExists))
        {
            return MessageIngressDecision.Block("Estou terminando de processar sua mensagem anterior. Me dá alguns segundos e já continuo por aqui.");
        }

        return MessageIngressDecision.Allow(new RedisLockLease(db, lockKey, lockValue));
    }

    private MessageIngressDecision BeginWithMemory(string provider, string phone, string? providerMessageId)
    {
        if (!string.IsNullOrWhiteSpace(providerMessageId))
        {
            var dedupeKey = $"dedupe:{provider}:{providerMessageId}";
            if (memory.TryGetValue(dedupeKey, out _))
            {
                return MessageIngressDecision.Block();
            }

            memory.Set(dedupeKey, true, TimeSpan.FromSeconds(_options.DeduplicationSeconds));
        }

        var cooldownKey = $"cooldown:{phone}";
        if (memory.TryGetValue(cooldownKey, out _))
        {
            var notifiedKey = $"cooldown-notified:{phone}";
            if (memory.TryGetValue(notifiedKey, out _))
            {
                return MessageIngressDecision.Block();
            }

            memory.Set(notifiedKey, true, TimeSpan.FromSeconds(_options.CooldownSeconds));
            return MessageIngressDecision.Block(RateLimitReply());
        }

        var counterKey = $"rate:{phone}";
        var current = memory.Get<int?>(counterKey) ?? 0;
        current += 1;
        memory.Set(counterKey, current, TimeSpan.FromSeconds(_options.WindowSeconds));
        if (current > _options.MaxMessages)
        {
            memory.Set(cooldownKey, true, TimeSpan.FromSeconds(_options.CooldownSeconds));
            return MessageIngressDecision.Block(RateLimitReply());
        }

        var lockKey = $"lock:{phone}";
        if (memory.TryGetValue(lockKey, out _))
        {
            return MessageIngressDecision.Block("Estou terminando de processar sua mensagem anterior. Me dá alguns segundos e já continuo por aqui.");
        }

        memory.Set(lockKey, true, TimeSpan.FromSeconds(_options.MessageLockSeconds));
        return MessageIngressDecision.Allow(new MemoryLockLease(memory, lockKey));
    }

    private static string RateLimitReply()
    {
        return "Recebi muitas mensagens em sequência. Vou pausar por alguns segundos para organizar tudo com segurança. Pode me chamar novamente em instantes.";
    }

    private sealed class RedisLockLease(IDatabase db, string key, string value) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            const string script = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
            await db.ScriptEvaluateAsync(script, [new RedisKey(key)], [new RedisValue(value)]);
        }
    }

    private sealed class MemoryLockLease(IMemoryCache memory, string key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            memory.Remove(key);
            return ValueTask.CompletedTask;
        }
    }
}
