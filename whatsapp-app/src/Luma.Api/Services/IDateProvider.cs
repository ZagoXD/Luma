namespace Luma.Api.Services;

public interface IDateProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDateProvider : IDateProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
