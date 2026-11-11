using Luma.Api.Services.Assistant;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Luma.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")))
            Skip = "Requer banco PostgreSQL isolado em LUMA_INTEGRATION_DATABASE.";
    }
}

public sealed class PostgresKnowledgeTests
{
    [PostgresFact]
    public async Task Published_corpus_passes_retrieval_evaluations_without_model_or_embedding_cost()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE") }).Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var embeddings = new CountingEmbedding();
        var store = new KnowledgeStore(config, Options.Create(new RagOptions { Enabled = true, Dimensions = 3 }),
            embeddings, cache, NullLogger<KnowledgeStore>.Instance);
        await store.PublishAsync();
        embeddings.Fail = true;
        var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var cases = System.Text.Json.JsonSerializer.Deserialize<RetrievalCase[]>(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Knowledge/evaluation-cases.json")), json)!;
        foreach (var sample in cases)
        {
            var result = await store.SearchAsync(sample.Query, default);
            Assert.True(sample.ShouldAnswer == result.Success, "Recuperação incorreta para: " + sample.Query);
            if (!sample.ShouldAnswer) { Assert.True(result.Fixed); continue; }
            Assert.Contains(sample.ExpectedIds, id => result.Message.Contains(CorpusTitle(id), StringComparison.OrdinalIgnoreCase));
        }
        string CorpusTitle(string id)
        {
            var corpus = System.Text.Json.JsonSerializer.Deserialize<KnowledgeDocument[]>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Knowledge/corpus.json")), json)!;
            return corpus.First(document => document.Id.StartsWith(id, StringComparison.Ordinal)).Title;
        }
    }

    private sealed record RetrievalCase(string Query, string[] ExpectedIds, bool ShouldAnswer);

    [PostgresFact]
    public async Task Retrieval_filters_private_sources_and_rejects_lexical_matches_below_semantic_threshold()
    {
        var path = Path.Combine(Path.GetTempPath(), "luma-private-" + Guid.NewGuid() + ".json");
        try
        {
            var documents = new[] {
                new KnowledgeDocument("public", "Calendário", "Previsão menstrual é uma estimativa.", "https://www.gov.br/saude", Guid.NewGuid().ToString(), "approved"),
                new KnowledgeDocument("product", "Planos", "O Essencial custa R$ 20,00 no mensal.", "https://internal.example/strategy", "v1", "approved", Visibility: "assistant"),
                new KnowledgeDocument("secret", "Segredo", "Segredo financeiro confidencial.", "https://internal.example/secret", "v1", "approved", Visibility: "restricted")
            };
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(documents));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE") }).Build();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = new KnowledgeStore(config, Options.Create(new RagOptions { Enabled = true, Dimensions = 3, CorpusPath = path }),
                new CountingEmbedding(), cache, NullLogger<KnowledgeStore>.Instance);
            await store.PublishAsync();
            var result = await store.SearchAsync("Planos", default);
            Assert.DoesNotContain("Segredo", result.Message);
            Assert.DoesNotContain("internal.example", System.Text.Json.JsonSerializer.Serialize(result));
            Assert.DoesNotContain(result.Sources!, source => source.Id == "product");
            var missing = await store.SearchAsync("calendário quântico desconhecido", default);
            Assert.False(missing.Success);
            Assert.True(missing.Fixed);
        }
        finally { File.Delete(path); }
    }

    [PostgresFact]
    public async Task Publish_is_idempotent_hybrid_search_returns_sources_and_failed_release_keeps_previous()
    {
        var path = Path.Combine(Path.GetTempPath(), "luma-corpus-" + Guid.NewGuid() + ".json");
        try
        {
            var initialVersion = "v1-" + Guid.NewGuid();
            var documents = new[] { new KnowledgeDocument("cycle", "Calendário menstrual",
                "Previsões de menstruação são estimativas do ciclo menstrual.", "https://www.gov.br/saude", initialVersion, "approved") };
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(documents));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = Environment.GetEnvironmentVariable("LUMA_INTEGRATION_DATABASE")
            }).Build();
            var embedding = new CountingEmbedding();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = new KnowledgeStore(config, Options.Create(new RagOptions { Enabled = true, Dimensions = 3, CorpusPath = path }),
                embedding, cache, NullLogger<KnowledgeStore>.Instance);
            var release = await store.PublishAsync();
            Assert.Equal(release, await store.PublishAsync());
            Assert.Equal(1, embedding.Calls);
            var result = await store.SearchAsync("calendário menstrual", default);
            Assert.True(result.Success);
            Assert.Equal("cycle", Assert.Single(result.Sources!).Id);
            Assert.Equal(3, embedding.LastDimensions);
            embedding.Fail = true;
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new[] { documents[0] with { Version = initialVersion + "-v2" } }));
            await Assert.ThrowsAsync<HttpRequestException>(() => store.PublishAsync());
            var fallback = await store.SearchAsync("menstruação", default);
            Assert.True(fallback.Success);
            Assert.Equal(initialVersion, Assert.Single(fallback.Sources!).Version);
            Assert.False((await store.SearchAsync("astronomia galáctica", default)).Success);
            Assert.NotEqual(release, await RestoreAndPublish());
            async Task<string> RestoreAndPublish() { embedding.Fail = false; return await store.PublishAsync(); }
        }
        finally { File.Delete(path); }
    }

    private sealed class CountingEmbedding : IEmbeddingClient
    {
        public int Calls { get; private set; }
        public int LastDimensions { get; private set; }
        public bool Fail { get; set; }
        public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("simulated");
            LastDimensions = 3;
            return Task.FromResult(texts.Select(text => text.Contains("quântico") ? new float[] { 0, 1, 0 } : new float[] { 1, 0, 0 }).ToArray());
        }
    }
}
