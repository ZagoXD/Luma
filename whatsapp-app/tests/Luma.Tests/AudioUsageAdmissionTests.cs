using Luma.Api.Data;
using Luma.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Luma.Tests;

public sealed class AudioUsageAdmissionTests
{
    [Theory]
    [InlineData("basico", 30, false)]
    [InlineData("essencial", 61, false)]
    [InlineData("essencial", 60, true)]
    public async Task Limits_are_checked_before_transcription_and_replays_never_transcribe_again(string plan, int seconds, bool allowed)
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ledger = new UsageLedger(options, Options.Create(new UsageOptions()), TimeProvider.System);
        var context = new UsageContext { AccountId = Guid.NewGuid(), Plan = plan, MessageId = "SM-audio" };
        var speech = new SpeechSpy();
        var service = new WhatsAppAudioTranscriptionService(new Media(seconds), speech,
            NullLogger<WhatsAppAudioTranscriptionService>.Instance, ledger, context);
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["NumMedia"] = "1",
            ["MediaContentType0"] = "audio/wav",
            ["MediaUrl0"] = "https://offline.invalid/audio"
        });
        Assert.Equal(allowed, (await service.TryTranscribeAsync(form)).Success);
        Assert.False((await service.TryTranscribeAsync(form)).Success);
        Assert.Equal(allowed ? 1 : 0, speech.Calls);
        await using var db = new LumaDbContext(options);
        Assert.Equal(allowed ? 3 : 0, await db.UsageReceipts.CountAsync());
        Assert.Equal(allowed, context.Admitted);
    }

    private sealed class Media(int seconds) : ITwilioMediaDownloader
    {
        public Task<TwilioMediaDownloadResult> DownloadAsync(string url, CancellationToken ct = default)
            => Task.FromResult(new TwilioMediaDownloadResult(AudioDurationPolicyTests.Wave(seconds), "audio/wav"));
    }

    [Fact]
    public async Task Monthly_AI_exhaustion_rejects_audio_before_download_or_transcription()
    {
        var options = new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ledger = new UsageLedger(options, Options.Create(new UsageOptions { EssentialMonthlyMessages = 1 }), TimeProvider.System);
        var context = new UsageContext { AccountId = Guid.NewGuid(), Plan = "essencial", MessageId = "new-audio" };
        await ledger.ReserveAsync(context.AccountId.Value, "essencial", "previous", "ai", 1);
        var speech = new SpeechSpy();
        var service = new WhatsAppAudioTranscriptionService(new NeverDownload(), speech,
            NullLogger<WhatsAppAudioTranscriptionService>.Instance, ledger, context);
        var result = await service.TryTranscribeAsync(new FormCollection(new Dictionary<string, StringValues> {
            ["NumMedia"] = "1", ["MediaContentType0"] = "audio/wav", ["MediaUrl0"] = "https://offline.invalid/audio" }));
        Assert.Equal("monthly_limit", result.ErrorMessage);
        Assert.Equal(0, speech.Calls);
    }

    private sealed class NeverDownload : ITwilioMediaDownloader
    {
        public Task<TwilioMediaDownloadResult> DownloadAsync(string url, CancellationToken ct = default)
            => throw new Exception("A quota-rejected audio must never be downloaded.");
    }

    [Fact]
    public async Task Busy_decoder_rejects_before_downloading_or_transcribing()
    {
        using var gate = new AudioProcessingGate();
        Assert.True(gate.Slots.Wait(0)); Assert.True(gate.Slots.Wait(0));
        var speech = new SpeechSpy();
        var service = new WhatsAppAudioTranscriptionService(new Media(1), speech,
            NullLogger<WhatsAppAudioTranscriptionService>.Instance, processingGate: gate);
        var result = await service.TryTranscribeAsync(new FormCollection(new Dictionary<string, StringValues> {
            ["NumMedia"] = "1", ["MediaContentType0"] = "audio/wav", ["MediaUrl0"] = "https://offline.invalid/audio" }));
        Assert.Equal("audio_busy", result.ErrorMessage);
        Assert.Equal(0, speech.Calls);
        gate.Slots.Release(2);
    }
    private sealed class SpeechSpy : ISpeechToTextService
    {
        public int Calls;
        public Task<SpeechToTextResult> TranscribeAsync(byte[] bytes, string contentType, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new SpeechToTextResult(true, "menstruei hoje")); }
    }
}
