namespace Luma.Api.Services;

public sealed class OpenAiOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string ImageModel { get; set; } = "gpt-image-1";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 12;
    public int MaxOutputTokens { get; set; } = 700;
    public string ReasoningEffort { get; set; } = "none";
}
