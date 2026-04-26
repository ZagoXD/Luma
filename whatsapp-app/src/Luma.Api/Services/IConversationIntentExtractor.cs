namespace Luma.Api.Services;

public interface IConversationIntentExtractor
{
    Task<ConversationIntent?> ExtractAsync(string message, DateOnly today, CancellationToken cancellationToken = default);
}
