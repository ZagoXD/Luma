using System.Text.Json;
using Luma.Api.Services.Assistant;

namespace Luma.Tests;

public sealed class KnowledgeIngestionTests
{
    [Fact]
    public async Task Real_pdf_import_is_reproducible_and_hash_changes_require_review()
    {
        var root = Path.Combine(Path.GetTempPath(), "luma-ingest-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
            page.AddText("Menstrual cycles vary. A calendar is an estimate, not a diagnosis. Ask your healthcare team.",
                12, new UglyToad.PdfPig.Core.PdfPoint(20, 700), font);
            var bytes = builder.Build();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(root, "example.pdf"), bytes);
            await File.WriteAllTextAsync(Path.Combine(root, "editorial.json"), "[]");
            var source = new PdfKnowledgeSource("example", "Manual", "https://www.gov.br/manual.pdf", "Teste",
                "v1", hash, "approved", "2026-11-10", "2099-01-01", "Public domain",
                [new("ciclo", "Ciclo", 1, 1)], "Nenhuma página excluída", CommercialUseAllowed: true);
            var manifest = Path.Combine(root, "sources.json");
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new KnowledgeManifest("editorial.json", [source])));
            var output = Path.Combine(root, "corpus.json");
            await KnowledgeIngestor.IngestAsync(manifest, root, output);
            var original = await File.ReadAllTextAsync(output);
            await KnowledgeIngestor.IngestAsync(manifest, root, output);
            Assert.Equal(original, await File.ReadAllTextAsync(output));
            var documents = JsonSerializer.Deserialize<KnowledgeDocument[]>(original, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(1, Assert.Single(documents).Page);
            Assert.NotNull(documents[0].ContentHash);
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new KnowledgeManifest("editorial.json", [source with { Sha256 = new string('0', 64) }])));
            await Assert.ThrowsAsync<InvalidDataException>(() => KnowledgeIngestor.IngestAsync(manifest, root, output));
            Assert.Equal(original, await File.ReadAllTextAsync(output));
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new KnowledgeManifest("editorial.json", [source with { CommercialUseAllowed = false }])));
            await Assert.ThrowsAsync<InvalidDataException>(() => KnowledgeIngestor.IngestAsync(manifest, root, output));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Restricted_and_expired_documents_never_enter_the_assistant_corpus()
    {
        var safe = new KnowledgeDocument("safe", "Planos", "Recursos disponíveis.", "https://ia-luma.com.br", "v1", "approved");
        var documents = KnowledgeCorpus.Validate([safe, safe with { Id = "secret", Visibility = "restricted", Content = "Segredo" },
            safe with { Id = "expired", ReviewDueAt = "2020-01-01" }]);
        Assert.Equal("safe", Assert.Single(documents).Id);
    }

    [Fact]
    public void Unknown_visibility_is_rejected_instead_of_becoming_public()
    {
        Assert.Throws<InvalidDataException>(() => KnowledgeCorpus.Validate([
            new("id", "Título", "Texto", "https://ia-luma.com.br", "v1", "approved", Visibility: "privatte")]));
    }

    [Fact]
    public void Chunking_is_bounded_deterministic_and_preserves_page_provenance()
    {
        var source = new PdfKnowledgeSource("manual", "Manual", "https://www.gov.br/manual.pdf", "Ministério da Saúde",
            "v1", new string('a', 64), "approved", "2026-11-10", "2099-01-01", "Reprodução autorizada com fonte",
            [new("ciclo", "Ciclo menstrual", 2, 2)], "Capa e formulários fora do escopo");
        var page = string.Join("\n\n", Enumerable.Repeat("O calendário é uma estimativa do ciclo menstrual. Não confirma gravidez.", 80));
        var first = KnowledgeChunker.FromPage(source, source.Sections[0], 2, page);
        var second = KnowledgeChunker.FromPage(source, source.Sections[0], 2, page);
        Assert.True(first.Count > 1);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.All(first, chunk => { Assert.InRange(chunk.Content.Length, 50, 2400); Assert.Equal(2, chunk.Page); Assert.Equal("manual", chunk.SourceId); });
        Assert.Equal(first.Count, first.Select(chunk => chunk.Id).Distinct().Count());
        Assert.All(first, chunk => Assert.DoesNotContain('\uFFFD', chunk.Content));
    }

    [Fact]
    public void Corrupt_text_is_not_silently_published()
    {
        Assert.Throws<InvalidDataException>(() => KnowledgeChunker.Normalize("Texto corrompido \uFFFD sem leitura confiável."));
    }

    [Theory]
    [InlineData("http://www.gov.br/manual.pdf")]
    [InlineData("https://127.0.0.1/manual.pdf")]
    [InlineData("https://www.gov.br.evil.example/manual.pdf")]
    public void Importer_rejects_untrusted_download_endpoints(string url)
        => Assert.Throws<InvalidDataException>(() => KnowledgeIngestor.ValidateDownloadUrl(url));
}
