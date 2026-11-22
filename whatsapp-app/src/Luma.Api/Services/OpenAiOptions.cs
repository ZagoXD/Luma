namespace Luma.Api.Services;

public sealed class OpenAiOptions
{
    public const string DefaultImageModel = "gpt-image-2.6-flare";
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string ImageModel { get; set; } = DefaultImageModel;
    public string ImageQuality { get; set; } = "low";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 12;
    public int MaxOutputTokens { get; set; } = 700;
    public string ReasoningEffort { get; set; } = "none";
}
