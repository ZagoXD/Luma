using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class R2Options
{
    public string AccountId { get; set; } = string.Empty;
    public string BucketName { get; set; } = "luma";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string BabyImagePrefix { get; set; } = "baby-image-generation";
    public int ImageRetentionDays { get; set; } = 1;
    public int ImageGenerationTimeoutSeconds { get; set; } = 60;
}

public sealed record BabyImageResult(bool IsAvailable, string? PublicUrl, string Message);

public interface IBabyImageService
{
    Task<BabyImageResult> GenerateAsync(BabyDevelopmentInfo development, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class DisabledBabyImageService : IBabyImageService
{
    public static readonly DisabledBabyImageService Instance = new();

    public Task<BabyImageResult> GenerateAsync(BabyDevelopmentInfo development, Guid userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BabyImageResult(
            false,
            null,
            "Consigo explicar o tamanho do bebê por texto, mas a geração de imagem ainda não está configurada neste ambiente."));
    }
}

public sealed class BabyImageService(
    HttpClient httpClient,
    IOptions<OpenAiOptions> openAiOptions,
    IOptions<R2Options> r2Options,
    ILogger<BabyImageService> logger) : IBabyImageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OpenAiOptions _openAi = openAiOptions.Value;
    private readonly R2Options _r2 = r2Options.Value;

    public async Task<BabyImageResult> GenerateAsync(BabyDevelopmentInfo development, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
        {
            return await DisabledBabyImageService.Instance.GenerateAsync(development, userId, cancellationToken);
        }

        try
        {
            using var imageTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            imageTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_r2.ImageGenerationTimeoutSeconds, 10, 120)));
            var bytes = await GenerateOpenAiImageAsync(development, imageTimeout.Token);
            var key = $"{_r2.BabyImagePrefix.Trim('/')}/{userId:N}/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.png";
            await UploadToR2Async(key, bytes, cancellationToken);

            return new BabyImageResult(
                true,
                $"{_r2.PublicBaseUrl.TrimEnd('/')}/{key}",
                "Imagem gerada com uma comparação educativa de tamanho aproximado.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or AmazonS3Exception)
        {
            logger.LogWarning(ex, "Baby image generation failed.");
            return new BabyImageResult(
                false,
                null,
                "A imagem pode levar um pouco mais para ficar pronta. Para não te deixar sem resposta no WhatsApp, vou te passar a estimativa por texto agora.");
        }
    }

    private bool IsConfigured()
    {
        return _openAi.Enabled
            && !string.IsNullOrWhiteSpace(_openAi.ApiKey)
            && !string.IsNullOrWhiteSpace(_r2.AccessKeyId)
            && !string.IsNullOrWhiteSpace(_r2.SecretAccessKey)
            && !string.IsNullOrWhiteSpace(_r2.Endpoint)
            && !string.IsNullOrWhiteSpace(_r2.PublicBaseUrl)
            && !string.IsNullOrWhiteSpace(_r2.BucketName);
    }

    private async Task<byte[]> GenerateOpenAiImageAsync(BabyDevelopmentInfo development, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildImagesUri(_openAi.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = string.IsNullOrWhiteSpace(_openAi.ImageModel) ? "gpt-image-1" : _openAi.ImageModel,
            prompt = $"""
Crie uma imagem educativa, acolhedora e não médica, em estilo ilustração limpa, mostrando uma comparação de tamanho fetal aproximado para {development.Week} semanas.
Use uma escala visual simples com o bebê representado de forma respeitosa e abstrata, ao lado de {development.Comparison}.
Não inclua texto na imagem, sangue, nudez, aparência realista explícita, diagnóstico ou conteúdo médico alarmista.
""",
            size = "1024x1024"
        }, options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var b64 = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("b64_json")
            .GetString();

        if (string.IsNullOrWhiteSpace(b64))
        {
            throw new JsonException("OpenAI image response did not include b64_json.");
        }

        return Convert.FromBase64String(b64);
    }

    private async Task UploadToR2Async(string key, byte[] bytes, CancellationToken cancellationToken)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = _r2.Endpoint.TrimEnd('/'),
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        };

        using var client = new AmazonS3Client(
            new BasicAWSCredentials(_r2.AccessKeyId, _r2.SecretAccessKey),
            config);

        await using var stream = new MemoryStream(bytes);
        var request = new PutObjectRequest
        {
            BucketName = _r2.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = "image/png",
            UseChunkEncoding = false,
            DisablePayloadSigning = true,
            AutoCloseStream = false
        };
        request.Headers.ContentLength = bytes.Length;

        await client.PutObjectAsync(request, cancellationToken);
    }

    private static Uri BuildImagesUri(string baseUrl)
    {
        var normalized = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
        return new Uri(new Uri(normalized), "images/generations");
    }
}
