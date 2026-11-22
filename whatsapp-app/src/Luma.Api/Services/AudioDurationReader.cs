namespace Luma.Api.Services;

public static class AudioDurationReader
{
    public static int Seconds(byte[] bytes, string contentType)
    {
        var extension = contentType.Split(';')[0].ToLowerInvariant() switch
        {
            "audio/ogg" or "application/ogg" or "audio/opus" => "ogg", "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/wav" or "audio/x-wav" => "wav", "audio/mp4" or "audio/x-m4a" => "m4a",
            "audio/aac" => "aac", _ => throw new InvalidDataException("Formato de áudio não suportado.")
        };
        using var file = TagLib.File.Create(new AudioFile(bytes, "audio." + extension));
        var seconds = file.Properties.Duration.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds <= 0) throw new InvalidDataException("Duração de áudio inválida.");
        return checked((int)Math.Ceiling(seconds));
    }

    private sealed class AudioFile(byte[] bytes, string name) : TagLib.File.IFileAbstraction
    {
        public string Name => name;
        public Stream ReadStream => new MemoryStream(bytes, false);
        public Stream WriteStream => throw new NotSupportedException();
        public void CloseStream(Stream stream) => stream.Dispose();
    }
}
