using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Luma.Api.Services;

public sealed class AudioLimitException : Exception;
public sealed class AudioProcessingGate : IDisposable
{
    public SemaphoreSlim Slots { get; } = new(2, 2);
    public void Dispose() => Slots.Dispose();
}

public interface IAudioPreparer
{
    Task<TwilioMediaDownloadResult> PrepareAsync(byte[] input, CancellationToken ct);
}

public sealed class FfmpegAudioPreparer(IConfiguration configuration, IOptions<UsageOptions> options) : IAudioPreparer
{
    public async Task<TwilioMediaDownloadResult> PrepareAsync(byte[] input, CancellationToken ct)
    {
        var seconds = Math.Clamp(options.Value.MaxAudioSeconds, 1, 120);
        const int bytesPerSecond = 16000 * 2;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var start = new ProcessStartInfo(configuration["Audio:FfmpegPath"] ?? "ffmpeg")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // The decoder receives no provider credentials, URLs or filesystem input.
        start.Environment.Clear();
        start.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
        if (OperatingSystem.IsWindows()) start.Environment["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "pipe",
            "-format_whitelist", "ogg,mp3,wav,mov,aac,amr,matroska,webm", "-threads", "1", "-i", "pipe:0",
            "-map", "0:a:0", "-vn", "-sn", "-dn", "-af", "aresample=16000,asetpts=N/SR/TB", "-ac", "1",
            "-ar", "16000", "-threads", "1", "-t", (seconds + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-f", "s16le", "pipe:1" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Audio decoder unavailable.");
        using var pcm = new MemoryStream();
        try
        {
            async Task WriteInput()
            {
                try { await process.StandardInput.BaseStream.WriteAsync(input, token); }
                catch (IOException) { /* A decoder may stop after reaching the output cap. */ }
                finally { process.StandardInput.Close(); }
            }
            async Task ReadOutput()
            {
                var buffer = new byte[8192];
                int read;
                while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, token)) > 0)
                {
                    if (pcm.Length + read > (seconds + 1) * bytesPerSecond) throw new AudioLimitException();
                    await pcm.WriteAsync(buffer.AsMemory(0, read), token);
                }
            }
            async Task DrainErrors()
            {
                var buffer = new char[1024];
                while (await process.StandardError.ReadAsync(buffer, token) > 0) { }
            }
            await Task.WhenAll(WriteInput(), ReadOutput(), DrainErrors(), process.WaitForExitAsync(token));
            if (process.ExitCode != 0 || pcm.Length == 0) throw new InvalidDataException("Invalid audio.");
            if (pcm.Length > seconds * bytesPerSecond) throw new AudioLimitException();
            using var wave = new MemoryStream();
            using var writer = new BinaryWriter(wave);
            writer.Write("RIFF"u8); writer.Write(checked((int)pcm.Length + 36)); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
            writer.Write(bytesPerSecond); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(checked((int)pcm.Length)); writer.Write(pcm.ToArray());
            return new(wave.ToArray(), "audio/wav");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
