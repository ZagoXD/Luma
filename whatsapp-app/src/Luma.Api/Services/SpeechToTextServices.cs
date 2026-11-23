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
                logger.LogWarning("ElevenLabs STT failed with status {StatusCode}.", (int)response.StatusCode);
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        cancellationToken = timeout.Token;
        if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken))
        {
            throw new InvalidOperationException("Twilio credentials are required to download incoming media.");
        }

        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "api.twilio.com" || uri.Port != 443 || uri.UserInfo.Length > 0
            || !uri.AbsolutePath.StartsWith($"/2010-04-01/Accounts/{_accountSid}/Messages/", StringComparison.Ordinal))
            throw new InvalidOperationException("Incoming media must belong to this Twilio account.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twilio media download failed with status {StatusCode}.", (int)response.StatusCode);
            throw new HttpRequestException($"Twilio media download failed with status {(int)response.StatusCode}.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        const int maxBytes = 10 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidOperationException("Audio too large.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maxBytes) throw new InvalidOperationException("Audio too large.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return new TwilioMediaDownloadResult(output.ToArray(), contentType);
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
    ILogger<WhatsAppAudioTranscriptionService> logger,
    UsageLedger? usage = null,
    UsageContext? usageContext = null,
    IAudioPreparer? audioPreparer = null,
    AudioProcessingGate? processingGate = null) : IWhatsAppAudioTranscriptionService
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

        if (processingGate is not null && !await processingGate.Slots.WaitAsync(0, cancellationToken))
            return new(true, false, null, "audio_busy");

        try
        {
            if (usage is not null)
            {
                if (usageContext?.AccountId is not Guid owner || usageContext.Plan != "essencial") return new(true, false, null, "audio_limit");
                var remaining = await usage.SnapshotAsync(owner, usageContext.Plan, cancellationToken);
                if (remaining.DailyUsed >= remaining.DailyLimit) return new(true, false, null, "daily_limit");
                if (remaining.MonthlyUsed >= remaining.MonthlyLimit) return new(true, false, null, "monthly_limit");
                if (remaining.AudioSecondsUsed >= remaining.MonthlyAudioSeconds) return new(true, false, null, "audio_limit");
            }
            var media = await mediaDownloader.DownloadAsync(audio.Value.MediaUrl, cancellationToken);
            if (audioPreparer is not null) media = await audioPreparer.PrepareAsync(media.Bytes, cancellationToken);
            if (usage is not null)
            {
                if (usageContext?.AccountId is not Guid accountId) return new(true, false, null, "audio_limit");
                var seconds = AudioDurationReader.Seconds(media.Bytes, media.ContentType);
                if (seconds > usage.Policy.MaxAudioSeconds) return new(true, false, null, "audio_limit");
                var admission = await usage.ReserveAudioAsync(accountId, usageContext.Plan, usageContext.MessageId, seconds, cancellationToken);
                if (!admission.Allowed || admission.Duplicate) return new(true, false, null, "daily_limit");
                usageContext.Admitted = true;
            }
            var callId = usage is null ? null : await usage.StartCallAsync(usageContext, "elevenlabs", "speech-to-text", 0, cancellationToken);
            var transcript = await speechToText.TranscribeAsync(media.Bytes, media.ContentType, cancellationToken);
            if (usage is not null && transcript.Success) await usage.CompleteCallAsync(callId, 0, 0, 0, null, cancellationToken);
            return transcript.Success && !string.IsNullOrWhiteSpace(transcript.Text)
                ? new AudioTranscriptionResult(true, true, transcript.Text.Trim())
                : new AudioTranscriptionResult(true, false, null, transcript.ErrorMessage ?? "empty_transcript");
        }
        catch (AudioLimitException) { return new(true, false, null, "audio_limit"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException or IOException or System.ComponentModel.Win32Exception or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            logger.LogWarning(ex, "WhatsApp audio transcription failed.");
            return new AudioTranscriptionResult(true, false, null, "audio_transcription_failed");
        }
        finally { processingGate?.Slots.Release(); }
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
