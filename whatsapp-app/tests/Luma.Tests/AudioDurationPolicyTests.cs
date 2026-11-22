using Luma.Api.Services;
using Xunit;

namespace Luma.Tests;

public class AudioDurationPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(61)]
    public void MeasuresWaveWithoutExternalService(int seconds)
        => Assert.Equal(seconds, AudioDurationReader.Seconds(Wave(seconds), "audio/wav"));

    internal static byte[] Wave(int seconds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataSize = seconds * 8000 * 2;
        writer.Write("RIFF"u8); writer.Write(dataSize + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
        writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(dataSize); writer.Write(new byte[dataSize]);
        return stream.ToArray();
    }

    [Fact]
    public void MalformedAudioDoesNotBecomeAFreeTranscription()
        => Assert.ThrowsAny<Exception>(() => AudioDurationReader.Seconds([1, 2, 3], "audio/ogg"));
}
