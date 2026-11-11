using System.Text.Json;
using Luma.Api.Services.Assistant;

namespace Luma.Tests;

public sealed class KnowledgeCorpusTests
{
    [Fact]
    public void Corpus_rejects_missing_provenance_and_duplicate_ids()
    {
        var documents = new[] { new KnowledgeDocument("cycle", "Ciclo", "Texto", "", "1", "approved") };
        Assert.Throws<InvalidDataException>(() => KnowledgeCorpus.Validate(documents));
        var valid = documents[0] with { SourceUrl = "https://www.gov.br/saude" };
        Assert.Throws<InvalidDataException>(() => KnowledgeCorpus.Validate([valid, valid]));
    }

    [Fact]
    public void Corpus_excludes_drafts_and_has_deterministic_version()
    {
        var approved = new KnowledgeDocument("cycle", "Ciclo", "Texto", "https://www.gov.br/saude", "1", "approved");
        var draft = approved with { Id = "draft", Status = "draft" };
        Assert.Single(KnowledgeCorpus.Validate([draft, approved]));
        Assert.Equal(KnowledgeCorpus.Version([approved]), KnowledgeCorpus.Version([approved]));
        Assert.NotEqual(KnowledgeCorpus.Version([approved]), KnowledgeCorpus.Version([approved with { Content = "Outro" }]));
    }

    [Fact]
    public void Embeddings_reject_wrong_dimensions_and_non_finite_values()
    {
        Assert.Throws<InvalidDataException>(() => KnowledgeCorpus.Vector([1, 2], 3));
        Assert.Throws<InvalidDataException>(() => KnowledgeCorpus.Vector([float.NaN], 1));
        Assert.Equal("[0.5,1]", KnowledgeCorpus.Vector([0.5f, 1], 2));
    }
}
