using Luma.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class FfmpegAudioPreparationTests
{
    [FfmpegFact]
    public async Task Decodes_real_samples_without_network_or_temporary_files()
    {
        var service = Create();
        var output = await service.PrepareAsync(AudioDurationPolicyTests.Wave(2), default);
        Assert.Equal("audio/wav", output.ContentType);
        Assert.Equal(2, AudioDurationReader.Seconds(output.Bytes, output.ContentType));
    }

    [FfmpegFact]
    public async Task Falsified_byte_rate_does_not_allow_more_than_sixty_seconds_to_reach_transcription()
    {
        var forged = AudioDurationPolicyTests.Wave(61);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(28, 4), 1_600_000);
        await Assert.ThrowsAsync<AudioLimitException>(() => Create().PrepareAsync(forged, default));
    }

    [FfmpegFact]
    public async Task Corrupt_files_fail_without_transcription()
        => await Assert.ThrowsAsync<InvalidDataException>(() => Create().PrepareAsync([1, 2, 3], default));

    private static FfmpegAudioPreparer Create() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Audio:FfmpegPath"] = Environment.GetEnvironmentVariable("LUMA_FFMPEG") ?? "ffmpeg" }).Build(),
        Options.Create(new UsageOptions()));
}

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        var executable = Environment.GetEnvironmentVariable("LUMA_FFMPEG");
        if (executable is not null && File.Exists(executable)) return;
        if ((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(path => File.Exists(Path.Combine(path, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg")))) return;
        Skip = "Install FFmpeg or set LUMA_FFMPEG for native audio validation.";
    }
}
