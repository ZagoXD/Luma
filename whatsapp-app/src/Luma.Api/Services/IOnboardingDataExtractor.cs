namespace Luma.Api.Services;

public interface IOnboardingDataExtractor
{
    Task<OnboardingExtraction?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default);
}
