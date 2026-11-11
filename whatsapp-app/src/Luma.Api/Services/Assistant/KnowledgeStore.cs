using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Luma.Api.Services.Assistant;

public sealed class RagOptions
{
    public bool Enabled { get; set; }
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 512;
    public string CorpusPath { get; set; } = "Knowledge/corpus.json";
    public double MinSimilarity { get; set; } = 0.45;
}

public sealed record KnowledgeDocument(string Id, string Title, string Content, string SourceUrl, string Version, string Status,
    string Visibility = "public", string? SourceId = null, int? Page = null, string? ContentHash = null,
    string? ReviewedAt = null, string? ReviewDueAt = null, string ReviewLevel = "editorial",
    string? Attribution = null, string? LicenseUrl = null);

public static class KnowledgeCorpus
{
    public static IReadOnlyList<KnowledgeDocument> Validate(IEnumerable<KnowledgeDocument> documents)
    {
        var all = documents.ToArray();
        if (all.Any(document => document.Visibility is not ("public" or "assistant" or "restricted")))
            throw new InvalidDataException("Classificação de acesso desconhecida.");
        var approved = all.Where(document => document.Status == "approved" && document.Visibility != "restricted"
            && (document.ReviewDueAt is null || DateOnly.ParseExact(document.ReviewDueAt, "yyyy-MM-dd", CultureInfo.InvariantCulture) >= DateOnly.FromDateTime(DateTime.UtcNow))).ToArray();
        if (approved.Length == 0 || approved.Select(document => document.Id).Distinct().Count() != approved.Length
            || approved.Any(document => string.IsNullOrWhiteSpace(document.Id) || document.Id.Length > 100
                || string.IsNullOrWhiteSpace(document.Title) || document.Title.Length > 200
                || string.IsNullOrWhiteSpace(document.Version) || string.IsNullOrWhiteSpace(document.Content)
                || document.Content.Length > 4000
                || !Uri.TryCreate(document.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new InvalidDataException("Acervo vazio ou documento sem identificação, fonte HTTPS, versão ou conteúdo válido.");
        return approved;
    }

    public static string Version(IEnumerable<KnowledgeDocument> documents)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(documents.OrderBy(document => document.Id))))).ToLowerInvariant();

    public static string Vector(float[] vector, int dimensions)
    {
        if (vector.Length != dimensions || vector.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Embedding com dimensão ou valores inválidos.");
        return "[" + string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
    }
}

public interface IEmbeddingClient
{
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

public sealed class OpenAiEmbeddingClient(HttpClient http, IOptions<OpenAiOptions> openAi, IOptions<RagOptions> rag) : IEmbeddingClient
{
    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(openAi.Value.ApiKey))
            throw new InvalidOperationException("OPENAI_API_KEY é necessária para gerar embeddings.");
        using var request = new HttpRequestMessage(HttpMethod.Post, openAi.Value.BaseUrl.TrimEnd('/') + "/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAi.Value.ApiKey);
        request.Content = JsonContent.Create(new { model = rag.Value.EmbeddingModel, input = texts, dimensions = rag.Value.Dimensions });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Falha na API de embeddings: " + (int)response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var result = json.RootElement.GetProperty("data").EnumerateArray().OrderBy(item => item.GetProperty("index").GetInt32())
            .Select(item => item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray()).ToArray();
        if (result.Length != texts.Count) throw new InvalidDataException("Quantidade inesperada de embeddings.");
        foreach (var vector in result) KnowledgeCorpus.Vector(vector, rag.Value.Dimensions);
        return result;
    }
}

public sealed class KnowledgeStore(IConfiguration config, IOptions<RagOptions> options,
    IEmbeddingClient embeddings, IMemoryCache cache, ILogger<KnowledgeStore> logger)
{
    private readonly RagOptions _options = options.Value;
    public const string NoEvidence = "Não tenho informação segura suficiente na minha base para responder isso. Prefiro não arriscar uma orientação incorreta. Se for uma dúvida sobre sua saúde, procure um profissional de saúde; posso ajudar a organizar suas dúvidas e registros.";

    private async Task<NpgsqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(config.GetConnectionString("DefaultConnection"));
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<string> PublishAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled) return "disabled";
        if (_options.Dimensions is < 1 or > 2000) throw new InvalidDataException("Dimensão do RAG inválida.");
        var path = Path.Combine(AppContext.BaseDirectory, _options.CorpusPath);
        var documents = KnowledgeCorpus.Validate(JsonSerializer.Deserialize<KnowledgeDocument[]>(
            await File.ReadAllTextAsync(path, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []);
        var version = KnowledgeCorpus.Version(documents) + ":" + _options.EmbeddingModel + ":" + _options.Dimensions;
        await using var db = await ConnectAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(ct);
        await using (var mutex = new NpgsqlCommand("SELECT pg_advisory_xact_lock(69420817)", db, transaction))
            await mutex.ExecuteNonQueryAsync(ct);
        await using (var schema = new NpgsqlCommand("""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS luma_knowledge (
                id text NOT NULL, title text NOT NULL, content text NOT NULL, source_url text NOT NULL,
                version text NOT NULL, release text NOT NULL, embedding vector NOT NULL,
                search tsvector GENERATED ALWAYS AS (to_tsvector('portuguese', title || ' ' || content)) STORED,
                PRIMARY KEY (release,id)
            );
            DO $$ BEGIN
              IF EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='luma_knowledge'::regclass AND contype='p' AND cardinality(conkey)=1) THEN
                ALTER TABLE luma_knowledge DROP CONSTRAINT luma_knowledge_pkey;
                ALTER TABLE luma_knowledge ADD PRIMARY KEY(release,id);
              END IF;
            END $$;
            CREATE TABLE IF NOT EXISTS luma_knowledge_active (
                id integer PRIMARY KEY CHECK(id=1), release text NOT NULL);
            ALTER TABLE luma_knowledge ADD COLUMN IF NOT EXISTS visibility text NOT NULL DEFAULT 'public';
            ALTER TABLE luma_knowledge ADD COLUMN IF NOT EXISTS metadata jsonb NOT NULL DEFAULT '{}'::jsonb;
            CREATE INDEX IF NOT EXISTS ix_luma_knowledge_search ON luma_knowledge USING gin(search);
            """, db, transaction))
            await schema.ExecuteNonQueryAsync(ct);
        await using (var check = new NpgsqlCommand("SELECT count(*) FROM luma_knowledge WHERE release=@release", db, transaction))
        {
            check.Parameters.AddWithValue("release", version);
            if ((long)(await check.ExecuteScalarAsync(ct))! == documents.Count)
            {
                await ActivateAsync(db, transaction, version, ct);
                await transaction.CommitAsync(ct);
                return version;
            }
        }
        var vectors = await embeddings.EmbedAsync(documents.Select(document => document.Title + "\n" + document.Content).ToArray(), ct);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            await using var insert = new NpgsqlCommand("""
                INSERT INTO luma_knowledge(id,title,content,source_url,version,release,embedding,visibility,metadata)
                VALUES (@id,@title,@content,@url,@version,@release,@vector::vector,@visibility,@metadata::jsonb)
                ON CONFLICT(release,id) DO UPDATE SET embedding=excluded.embedding
                """, db, transaction);
            insert.Parameters.AddWithValue("id", document.Id);
            insert.Parameters.AddWithValue("title", document.Title);
            insert.Parameters.AddWithValue("content", document.Content);
            insert.Parameters.AddWithValue("url", document.SourceUrl);
            insert.Parameters.AddWithValue("version", document.Version);
            insert.Parameters.AddWithValue("release", version);
            insert.Parameters.AddWithValue("vector", KnowledgeCorpus.Vector(vectors[index], _options.Dimensions));
            insert.Parameters.AddWithValue("visibility", document.Visibility);
            insert.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new { document.SourceId, document.Page,
                document.ContentHash, document.ReviewedAt, document.ReviewDueAt, document.ReviewLevel, document.Attribution, document.LicenseUrl }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await ActivateAsync(db, transaction, version, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Published RAG release {Release} with {Count} chunks", version, documents.Count);
        return version;
    }

    private static async Task ActivateAsync(NpgsqlConnection db, NpgsqlTransaction transaction, string version, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO luma_knowledge_active(id,release) VALUES(1,@release)
            ON CONFLICT(id) DO UPDATE SET release=excluded.release
            """, db, transaction);
        command.Parameters.AddWithValue("release", version);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ToolOutcome> SearchAsync(string query, CancellationToken ct, string scope = "all")
    {
        if (scope is not ("all" or "product" or "health")) return new(false, NoEvidence, true);
        if (!_options.Enabled) return new(false, NoEvidence, true);
        query = query.Trim();
        if (query.Length is < 3 or > 600) return new(false, "Envie uma pergunta curta para consultar a base.");
        var vector = "";
        try
        {
            var key = "rag-vector:" + _options.EmbeddingModel + ":" + _options.Dimensions + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));
            var embedded = await cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return (await embeddings.EmbedAsync([query], ct))[0];
            });
            vector = KnowledgeCorpus.Vector(embedded!, _options.Dimensions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning("RAG embedding unavailable; using lexical retrieval ({ErrorType})", ex.GetType().Name);
        }
        await using var db = await ConnectAsync(ct);
        var sql = vector.Length == 0 ? """
            SELECT id,title,content,source_url,version,visibility,metadata FROM luma_knowledge
            WHERE release=(SELECT release FROM luma_knowledge_active WHERE id=1)
              AND visibility IN ('public','assistant')
              AND (@scope='all' OR (@scope='product' AND visibility='assistant') OR (@scope='health' AND visibility='public'))
              AND coalesce(metadata->>'reviewDueAt','9999-12-31')::date>=CURRENT_DATE
              AND search @@ plainto_tsquery('portuguese',@lexical)
            ORDER BY ts_rank_cd(search,plainto_tsquery('portuguese',@lexical)) DESC LIMIT 3
            """ : """
            WITH candidates AS (
              SELECT *, 1-(embedding <=> @vector::vector) AS similarity,
                ts_rank_cd(search,plainto_tsquery('portuguese',@lexical)) AS lexical
              FROM luma_knowledge WHERE vector_dims(embedding)=@dimensions AND release LIKE @release
                AND release=(SELECT release FROM luma_knowledge_active WHERE id=1)
                AND visibility IN ('public','assistant')
                AND (@scope='all' OR (@scope='product' AND visibility='assistant') OR (@scope='health' AND visibility='public'))
                AND coalesce(metadata->>'reviewDueAt','9999-12-31')::date>=CURRENT_DATE
            ), ranked AS (
              SELECT *, row_number() OVER(ORDER BY similarity DESC) AS semantic_rank,
                row_number() OVER(ORDER BY lexical DESC) AS lexical_rank
              FROM candidates WHERE similarity>=@minimum
            )
            SELECT id,title,content,source_url,version,visibility,metadata FROM ranked
            ORDER BY (1.0/(60+semantic_rank) + CASE WHEN lexical>0 THEN 1.0/(60+lexical_rank) ELSE 0 END) DESC LIMIT 3
            """;
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("lexical", query);
        command.Parameters.AddWithValue("scope", scope);
        if (vector.Length != 0)
        {
            command.Parameters.AddWithValue("vector", vector);
            command.Parameters.AddWithValue("minimum", _options.MinSimilarity);
            command.Parameters.AddWithValue("dimensions", _options.Dimensions);
            command.Parameters.AddWithValue("release", "%:" + _options.EmbeddingModel + ":" + _options.Dimensions);
        }
        await using var reader = await command.ExecuteReaderAsync(ct);
        var excerpts = new List<string>();
        var sources = new List<KnowledgeSource>();
        while (await reader.ReadAsync(ct))
        {
            if (reader.GetString(5) == "public")
            {
                using var metadata = JsonDocument.Parse(reader.GetString(6));
                sources.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(3), reader.GetString(4),
                    metadata.RootElement.TryGetProperty("attribution", out var attribution) ? attribution.GetString() : null,
                    metadata.RootElement.TryGetProperty("licenseUrl", out var licenseUrl) ? licenseUrl.GetString() : null));
            }
            excerpts.Add(reader.GetString(1) + "\n" + reader.GetString(2));
        }
        return excerpts.Count == 0
            ? new(false, NoEvidence, true)
            : new(true, string.Join("\n\n", excerpts), Sources: sources, ContextOnly: true);
    }
}
