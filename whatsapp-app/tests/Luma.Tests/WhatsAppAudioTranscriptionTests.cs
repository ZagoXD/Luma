using Luma.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Luma.Tests;

public sealed class WhatsAppAudioTranscriptionTests
{
    [Fact]
    public async Task TryTranscribeAsync_Ignores_message_without_audio_media()
    {
        var processor = new WhatsAppAudioTranscriptionService(
            new FakeTwilioMediaDownloader(Array.Empty<byte>(), "audio/ogg"),
            new FakeSpeechToTextService("menstruei hoje"),
            NullLogger<WhatsAppAudioTranscriptionService>.Instance);

        var result = await processor.TryTranscribeAsync(Form(("NumMedia", "1"), ("MediaContentType0", "image/jpeg"), ("MediaUrl0", "https://media.example/image")));

        Assert.False(result.Attempted);
        Assert.False(result.Success);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task TryTranscribeAsync_Downloads_audio_media_and_returns_transcript()
    {
        var audio = new byte[] { 1, 2, 3, 4 };
        var downloader = new FakeTwilioMediaDownloader(audio, "audio/ogg");
        var speech = new FakeSpeechToTextService("menstruei hoje");
        var processor = new WhatsAppAudioTranscriptionService(
            downloader,
            speech,
            NullLogger<WhatsAppAudioTranscriptionService>.Instance);

        var result = await processor.TryTranscribeAsync(Form(
            ("NumMedia", "1"),
            ("MediaContentType0", "audio/ogg"),
            ("MediaUrl0", "https://api.twilio.com/media/audio")));

        Assert.True(result.Attempted);
        Assert.True(result.Success);
        Assert.Equal("menstruei hoje", result.Text);
        Assert.Equal("https://api.twilio.com/media/audio", downloader.LastUrl);
        Assert.Equal(audio, speech.LastAudio);
        Assert.Equal("audio/ogg", speech.LastContentType);
    }

    [Fact]
    public void HasAudioMedia_Detects_audio_before_transcription()
    {
        Assert.True(WhatsAppAudioTranscriptionService.HasAudioMedia(Form(
            ("NumMedia", "1"),
            ("MediaContentType0", "audio/ogg"),
            ("MediaUrl0", "https://api.twilio.com/media/audio"))));

        Assert.False(WhatsAppAudioTranscriptionService.HasAudioMedia(Form(
            ("NumMedia", "1"),
            ("MediaContentType0", "image/jpeg"),
            ("MediaUrl0", "https://api.twilio.com/media/image"))));
    }

    [Fact]
    public async Task TryTranscribeAsync_Returns_attempted_failure_when_transcript_is_empty()
    {
        var processor = new WhatsAppAudioTranscriptionService(
            new FakeTwilioMediaDownloader([1, 2, 3], "audio/ogg"),
            new FakeSpeechToTextService(" "),
            NullLogger<WhatsAppAudioTranscriptionService>.Instance);

        var result = await processor.TryTranscribeAsync(Form(
            ("NumMedia", "1"),
            ("MediaContentType0", "audio/ogg"),
            ("MediaUrl0", "https://api.twilio.com/media/audio")));

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Null(result.Text);
    }

    private static IFormCollection Form(params (string Key, string Value)[] values)
    {
        return new FormCollection(values.ToDictionary(item => item.Key, item => new Microsoft.Extensions.Primitives.StringValues(item.Value)));
    }

    private sealed class FakeTwilioMediaDownloader(byte[] audio, string contentType) : ITwilioMediaDownloader
    {
        public string? LastUrl { get; private set; }

        public Task<TwilioMediaDownloadResult> DownloadAsync(string mediaUrl, CancellationToken cancellationToken = default)
        {
            LastUrl = mediaUrl;
            return Task.FromResult(new TwilioMediaDownloadResult(audio, contentType));
        }
    }

    private sealed class FakeSpeechToTextService(string transcript) : ISpeechToTextService
    {
        public byte[]? LastAudio { get; private set; }
        public string? LastContentType { get; private set; }

        public Task<SpeechToTextResult> TranscribeAsync(byte[] audio, string contentType, CancellationToken cancellationToken = default)
        {
            LastAudio = audio;
            LastContentType = contentType;
            return Task.FromResult(new SpeechToTextResult(!string.IsNullOrWhiteSpace(transcript), transcript.Trim()));
        }
    }
}
