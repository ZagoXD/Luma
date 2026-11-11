using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Luma.Api.Services.Assistant;

public sealed record PdfKnowledgeSection(string Id, string Title, int FirstPage, int LastPage);
public sealed record PdfKnowledgeSource(string Id, string Title, string DownloadUrl, string Publisher,
    string Version, string Sha256, string Status, string ReviewedAt, string ReviewDueAt, string License,
    IReadOnlyList<PdfKnowledgeSection> Sections, string ExclusionReason, bool CommercialUseAllowed = false, string? LicenseUrl = null);
public sealed record KnowledgeManifest(string EditorialPath, IReadOnlyList<PdfKnowledgeSource> Sources);

public static class KnowledgeChunker
{
    public static string Normalize(string text)
    {
        if (text.Contains('\uFFFD')) throw new InvalidDataException("Extração com caracteres corrompidos; revise o PDF ou aplique OCR antes da aprovação.");
        text = text.Normalize(NormalizationForm.FormC).Replace("\u00ad", "");
        text = Regex.Replace(text, @"[\p{Cc}\p{Cf}]", match => match.Value is "\r" or "\n" or "\t" ? match.Value : "");
        text = Regex.Replace(text, @"(?<=\p{L})-\s*\r?\n\s*(?=\p{Ll})", "");
        text = Regex.Replace(text, @"[ \t]+", " ");
        return Regex.Replace(text, @"\r?\n[ \t]*\r?\n+", "\n\n").Trim();
    }

    public static IReadOnlyList<KnowledgeDocument> FromPage(PdfKnowledgeSource source, PdfKnowledgeSection section, int page, string text)
    {
        text = Normalize(text);
        if (text.Length < 50) throw new InvalidDataException($"Página {page} sem texto suficiente; não será indexada silenciosamente.");
        var documents = new List<KnowledgeDocument>();
        var paragraphs = Regex.Split(text, @"\n\s*\n");
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length == 0) return;
            var content = current.ToString().Trim();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            documents.Add(new($"{source.Id}-{section.Id}-p{page}-{documents.Count + 1}",
                section.Title + " (p. " + page + ")", content, source.DownloadUrl + "#page=" + page, source.Version,
                source.Status, SourceId: source.Id, Page: page, ContentHash: hash,
                ReviewedAt: source.ReviewedAt, ReviewDueAt: source.ReviewDueAt,
                Attribution: source.Publisher + "; " + source.License + "; extração textual, sem endosso à Luma.", LicenseUrl: source.LicenseUrl));
            current.Clear();
        }
        foreach (var paragraph in paragraphs)
        {
            var remaining = paragraph.Trim();
            while (remaining.Length > 0)
            {
                if (current.Length + remaining.Length + 2 <= 2400)
                {
                    if (current.Length > 0) current.Append("\n\n");
                    current.Append(remaining);
                    break;
                }
                if (current.Length > 0) { Flush(); continue; }
                var end = remaining.LastIndexOf(' ', 2399);
                if (end < 1200) end = 2400;
                current.Append(remaining.AsSpan(0, end));
                Flush();
                remaining = remaining[end..].TrimStart();
            }
        }
        Flush();
        return documents;
    }
}

public static class KnowledgeIngestor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly string[] Hosts = ["www.gov.br", "bvsms.saude.gov.br", "bvs.saude.gov.br", "educapes.capes.gov.br", "womenshealth.gov", "www.womenshealth.gov"];
    public static Uri ValidateDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || !Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Origem de PDF não autorizada.");
        return uri;
    }

    public static async Task IngestAsync(string manifestPath, string originalsPath, string outputPath, CancellationToken ct = default)
    {
        var manifest = JsonSerializer.Deserialize<KnowledgeManifest>(await File.ReadAllTextAsync(manifestPath, ct), Json)
            ?? throw new InvalidDataException("Manifesto vazio.");
        var editorialPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, manifest.EditorialPath));
        var result = JsonSerializer.Deserialize<List<KnowledgeDocument>>(await File.ReadAllTextAsync(editorialPath, ct), Json) ?? [];
        Directory.CreateDirectory(originalsPath);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
        var reports = new List<object>();
        foreach (var source in manifest.Sources)
        {
            if (source.Status != "approved")
            {
                reports.Add(new { source.Id, source.Title, source.Status, source.License, source.ExclusionReason, chunks = 0 });
                continue;
            }
            if (!source.CommercialUseAllowed || Regex.IsMatch(source.License, @"\bNC\b|Non.?Commercial|Não.?Comercial", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Fonte sem liberação comercial explícita. Não publique documentos NC sem autorização.");
            var url = ValidateDownloadUrl(source.DownloadUrl);
            if (!Regex.IsMatch(source.Id, "^[a-z0-9-]{1,60}$") || !Regex.IsMatch(source.Sha256, "^[a-fA-F0-9]{64}$")
                || string.IsNullOrWhiteSpace(source.License) || string.IsNullOrWhiteSpace(source.ExclusionReason)
                || !DateOnly.TryParseExact(source.ReviewedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || !DateOnly.TryParseExact(source.ReviewDueAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Manifesto sem hash, licença, revisão ou justificativa de seleção.");
            var path = Path.Combine(originalsPath, source.Id + ".pdf");
            if (!File.Exists(path))
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 25_000_000) throw new InvalidDataException("PDF excede o limite de 25 MB.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var bytes = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(bytes, ct)) > 0)
                {
                    if (buffer.Length + read > 25_000_000) throw new InvalidDataException("PDF excede o limite de 25 MB.");
                    buffer.Write(bytes, 0, read);
                }
                var downloaded = buffer.ToArray();
                ValidatePdfBytes(downloaded, source.Sha256);
                await File.WriteAllBytesAsync(path, downloaded, ct);
            }
            var data = await File.ReadAllBytesAsync(path, ct);
            ValidatePdfBytes(data, source.Sha256);
            using var pdf = PdfDocument.Open(data);
            var selectedPages = new HashSet<int>();
            var countBefore = result.Count;
            foreach (var section in source.Sections)
            {
                if (!Regex.IsMatch(section.Id, "^[a-z0-9-]{1,40}$") || section.FirstPage < 1 || section.LastPage < section.FirstPage || section.LastPage > pdf.NumberOfPages)
                    throw new InvalidDataException("Seção com páginas ou identificação inválidas.");
                for (var page = section.FirstPage; page <= section.LastPage; page++)
                {
                    if (!selectedPages.Add(page)) throw new InvalidDataException("Página repetida no manifesto.");
                    result.AddRange(KnowledgeChunker.FromPage(source, section, page, ContentOrderTextExtractor.GetText(pdf.GetPage(page))));
                }
            }
            reports.Add(new { source.Id, source.Title, source.Publisher, source.DownloadUrl, source.Version, source.Sha256,
                source.License, source.ReviewedAt, source.ReviewDueAt, reviewLevel = "editorial", totalPages = pdf.NumberOfPages,
                indexedPages = selectedPages.Order().ToArray(), chunks = result.Count - countBefore,
                excludedPages = Enumerable.Range(1, pdf.NumberOfPages).Except(selectedPages).ToArray(), source.ExclusionReason });
        }
        var approved = KnowledgeCorpus.Validate(result);
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await AtomicWriteAsync(output, JsonSerializer.Serialize(approved, Json), ct);
        await AtomicWriteAsync(Path.Combine(Path.GetDirectoryName(output)!, "ingestion-report.json"), JsonSerializer.Serialize(reports, Json), ct);
        Console.WriteLine($"Acervo gerado: {approved.Count} trechos, {manifest.Sources.Count(source => source.Status == "approved")} PDFs indexados; {reports.Count} fontes inventariadas. Nenhuma chamada de IA foi feita na importação.");
    }

    public static void ValidatePdfBytes(byte[] bytes, string sha256)
    {
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PDF diferente da versão aprovada ou arquivo inválido. Revise antes de atualizar o hash.");
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken ct)
    {
        var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), ct); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
