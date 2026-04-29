using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class OpenAiResponsesClient(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiResponsesClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OpenAiOptions _options = options.Value;

    public async Task<T?> CreateStructuredAsync<T>(
        string schemaName,
        object schema,
        string developerPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return default;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, _options.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildResponsesUri(_options.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.Model,
            input = new object[]
            {
                new { role = "developer", content = developerPrompt },
                new { role = "user", content = userPrompt }
            },
            max_output_tokens = _options.MaxOutputTokens,
            reasoning = new { effort = _options.ReasoningEffort },
            text = new
            {
                verbosity = "low",
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    strict = true,
                    schema
                }
            }
        }, options: SerializerOptions);

        try
        {
            var response = await httpClient.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(linked.Token);
                logger.LogWarning("OpenAI Responses API failed with status {StatusCode}: {Body}", response.StatusCode, error);
                return default;
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(linked.Token), cancellationToken: linked.Token);
            var text = ExtractOutputText(document.RootElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(text, SerializerOptions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "OpenAI Responses API unavailable; using fallback.");
            return default;
        }
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static Uri BuildResponsesUri(string baseUrl)
    {
        var normalizedBaseUrl = baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : $"{baseUrl}/";

        return new Uri(new Uri(normalizedBaseUrl), "responses");
    }
}
