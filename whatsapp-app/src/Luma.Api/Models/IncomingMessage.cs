namespace Luma.Api.Models;

public sealed record IncomingMessage(
    string Provider,
    string From,
    string Body,
    string? ProviderMessageId);
