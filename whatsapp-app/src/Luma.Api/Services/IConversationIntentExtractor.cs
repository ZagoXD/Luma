namespace Luma.Api.Services;

public interface IConversationIntentExtractor
{
    Task<ConversationIntent?> ExtractAsync(
        string message,
        DateOnly today,
        ConversationContext? context = null,
        CancellationToken cancellationToken = default);
}
