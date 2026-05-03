using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class ElevenLabsOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string SpeechToTextModel { get; set; } = "scribe_v2";
    public string LanguageCode { get; set; } = "pt";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxAudioBytes { get; set; } = 10 * 1024 * 1024;
}

public sealed record SpeechToTextResult(bool Success, string? Text, string? ErrorMessage = null);

public interface ISpeechToTextService
{
    Task<SpeechToTextResult> TranscribeAsync(byte[] audio, string contentType, CancellationToken cancellationToken = default);
}

public sealed class ElevenLabsSpeechToTextService(
    HttpClient http,
    IOptions<ElevenLabsOptions> options,
    ILogger<ElevenLabsSpeechToTextService> logger) : ISpeechToTextService
{
    private readonly ElevenLabsOptions _options = options.Value;

    public async Task<SpeechToTextResult> TranscribeAsync(byte[] audio, string contentType, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new SpeechToTextResult(false, null, "elevenlabs_not_configured");
        }

        if (audio.Length == 0 || audio.Length > _options.MaxAudioBytes)
        {
            return new SpeechToTextResult(false, null, "invalid_audio_size");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120)));

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(_options.BaseUrl));
            request.Headers.Add("xi-api-key", _options.ApiKey);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(string.IsNullOrWhiteSpace(_options.SpeechToTextModel) ? "scribe_v2" : _options.SpeechToTextModel), "model_id");
            if (!string.IsNullOrWhiteSpace(_options.LanguageCode))
            {
                form.Add(new StringContent(_options.LanguageCode), "language_code");
            }

            var file = new ByteArrayContent(audio);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            form.Add(file, "file", BuildFileName(contentType));
            request.Content = form;

            using var response = await http.SendAsync(request, timeout.Token);
            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ElevenLabs STT failed with status {StatusCode}: {Payload}", (int)response.StatusCode, Truncate(payload));
                return new SpeechToTextResult(false, null, "elevenlabs_request_failed");
            }

            using var document = JsonDocument.Parse(payload);
            var text = document.RootElement.TryGetProperty("text", out var textProperty)
                ? textProperty.GetString()
                : null;

            return string.IsNullOrWhiteSpace(text)
                ? new SpeechToTextResult(false, null, "empty_transcript")
                : new SpeechToTextResult(true, text.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            logger.LogWarning(ex, "ElevenLabs STT transcription failed.");
            return new SpeechToTextResult(false, null, "elevenlabs_transcription_failed");
        }
    }

    private static Uri BuildUri(string baseUrl)
    {
        var normalized = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
        return new Uri(new Uri(normalized), "speech-to-text");
    }

    private static string BuildFileName(string contentType)
    {
        return MessageText.Normalize(contentType) switch
        {
            var value when value.Contains("ogg", StringComparison.Ordinal) => "audio.ogg",
            var value when value.Contains("mpeg", StringComparison.Ordinal) || value.Contains("mp3", StringComparison.Ordinal) => "audio.mp3",
            var value when value.Contains("mp4", StringComparison.Ordinal) => "audio.mp4",
            var value when value.Contains("wav", StringComparison.Ordinal) => "audio.wav",
            _ => "audio.bin"
        };
    }

    private static string Truncate(string value)
    {
        return value.Length <= 512 ? value : value[..512];
    }
}

public sealed record TwilioMediaDownloadResult(byte[] Bytes, string ContentType);

public interface ITwilioMediaDownloader
{
    Task<TwilioMediaDownloadResult> DownloadAsync(string mediaUrl, CancellationToken cancellationToken = default);
}

public sealed class TwilioMediaDownloader(HttpClient http, IConfiguration configuration, ILogger<TwilioMediaDownloader> logger) : ITwilioMediaDownloader
{
    private readonly string _accountSid = configuration.GetValue<string>("Twilio:AccountSid") ?? string.Empty;
    private readonly string _authToken = configuration.GetValue<string>("Twilio:AuthToken") ?? string.Empty;

    public async Task<TwilioMediaDownloadResult> DownloadAsync(string mediaUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken))
        {
            throw new InvalidOperationException("Twilio credentials are required to download incoming media.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        var auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

        using var response = await http.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twilio media download failed with status {StatusCode}.", (int)response.StatusCode);
            throw new HttpRequestException($"Twilio media download failed with status {(int)response.StatusCode}.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new TwilioMediaDownloadResult(bytes, contentType);
    }
}

public sealed record AudioTranscriptionResult(bool Attempted, bool Success, string? Text, string? ErrorMessage = null)
{
    public static AudioTranscriptionResult NotAttempted { get; } = new(false, false, null);
}

public interface IWhatsAppAudioTranscriptionService
{
    Task<AudioTranscriptionResult> TryTranscribeAsync(IFormCollection form, CancellationToken cancellationToken = default);
}

public sealed class WhatsAppAudioTranscriptionService(
    ITwilioMediaDownloader mediaDownloader,
    ISpeechToTextService speechToText,
    ILogger<WhatsAppAudioTranscriptionService> logger) : IWhatsAppAudioTranscriptionService
{
    public static bool HasAudioMedia(IFormCollection form)
    {
        return FindFirstAudio(form) is not null;
    }

    public async Task<AudioTranscriptionResult> TryTranscribeAsync(IFormCollection form, CancellationToken cancellationToken = default)
    {
        var audio = FindFirstAudio(form);
        if (audio is null)
        {
            return AudioTranscriptionResult.NotAttempted;
        }

        try
        {
            var media = await mediaDownloader.DownloadAsync(audio.Value.MediaUrl, cancellationToken);
            var transcript = await speechToText.TranscribeAsync(media.Bytes, media.ContentType, cancellationToken);
            return transcript.Success && !string.IsNullOrWhiteSpace(transcript.Text)
                ? new AudioTranscriptionResult(true, true, transcript.Text.Trim())
                : new AudioTranscriptionResult(true, false, null, transcript.ErrorMessage ?? "empty_transcript");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "WhatsApp audio transcription failed.");
            return new AudioTranscriptionResult(true, false, null, "audio_transcription_failed");
        }
    }

    private static (string MediaUrl, string ContentType)? FindFirstAudio(IFormCollection form)
    {
        var count = int.TryParse(form["NumMedia"].ToString(), out var parsed) ? parsed : 0;
        for (var i = 0; i < count; i++)
        {
            var contentType = form[$"MediaContentType{i}"].ToString();
            var mediaUrl = form[$"MediaUrl{i}"].ToString();
            if (!string.IsNullOrWhiteSpace(mediaUrl) && IsAudioContentType(contentType))
            {
                return (mediaUrl, contentType);
            }
        }

        return null;
    }

    private static bool IsAudioContentType(string contentType)
    {
        var normalized = MessageText.Normalize(contentType);
        return normalized.StartsWith("audio/", StringComparison.Ordinal)
            || normalized.Contains("ogg", StringComparison.Ordinal)
            || normalized.Contains("opus", StringComparison.Ordinal);
    }
}
