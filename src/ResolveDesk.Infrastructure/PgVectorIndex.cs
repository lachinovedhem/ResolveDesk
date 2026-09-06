using System.Globalization;
using Dapper;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// pgvector-backed semantic index. Availability is probed once and cached, because a plain PostgreSQL
/// without the extension is a supported deployment — the product falls back to keyword search rather
/// than failing.
/// </summary>
public sealed class PgVectorIndex(DbConnectionFactory factory) : ISemanticIndex
{
    private bool? _available;

    /// <summary>The content that gets embedded, as one expression reused by search and backfill.</summary>
    private const string ContentExpr = "t.title || E'\\n' || t.description || E'\\n' || COALESCE(t.resolution, '')";
    private const string HashExpr = "md5(" + ContentExpr + ")";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (_available is { } cached) return cached;
        try
        {
            const string sql = """
                SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector')
                   AND EXISTS (SELECT 1 FROM pg_class WHERE relname = 'ticket_embeddings');
                """;
            await using var conn = factory.Create();
            await conn.OpenAsync(ct);
            _available = await conn.ExecuteScalarAsync<bool>(sql);
        }
        catch
        {
            _available = false;
        }
        return _available.Value;
    }

    public async Task<bool> EnsureSchemaAsync(int dimensions, CancellationToken ct = default)
    {
        if (dimensions is < 1 or > 16000)
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "pgvector supports 1..16000 dimensions.");

        // `dimensions` is a validated integer from configuration, never request input — safe to inline,
        // and it has to be inlined because a column type cannot be parameterised.
        var ddl = $"""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS ticket_embeddings (
                ticket_id      bigint PRIMARY KEY REFERENCES tickets(id) ON DELETE CASCADE,
                content_hash   text NOT NULL,
                embedding      vector({dimensions}) NOT NULL,
                model          text NOT NULL,
                updated_at_utc timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ix_ticket_embeddings_hnsw
                ON ticket_embeddings USING hnsw (embedding vector_cosine_ops);
            """;

        try
        {
            await using var conn = factory.Create();
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync(ct);
            _available = true;
            return true;
        }
        catch
        {
            // Extension not installed, or the role lacks CREATE EXTENSION — keyword search still works.
            _available = false;
            return false;
        }
    }

    public async Task UpsertAsync(long ticketId, string contentHash, float[] embedding, string model, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct) || embedding.Length == 0) return;

        const string sql = """
            INSERT INTO ticket_embeddings (ticket_id, content_hash, embedding, model)
            VALUES (@ticketId, @contentHash, @embedding::vector, @model)
            ON CONFLICT (ticket_id) DO UPDATE
                SET content_hash = EXCLUDED.content_hash,
                    embedding    = EXCLUDED.embedding,
                    model        = EXCLUDED.model,
                    updated_at_utc = now();
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { ticketId, contentHash, embedding = ToVectorLiteral(embedding), model });
    }

    public async Task<IReadOnlyList<ResolutionSuggestion>> SearchAsync(
        float[] queryEmbedding, int limit, double minSimilarity, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct) || queryEmbedding.Length == 0) return [];

        // `<=>` is cosine distance, so similarity is 1 - distance. ORDER BY on the raw operator is what
        // lets the HNSW index serve the query.
        const string sql = """
            SELECT t.id AS "SourceTicketId", t.reference AS "SourceReference", t.title AS "Title",
                   COALESCE(t.resolution, '') AS "Resolution",
                   (1 - (e.embedding <=> @q::vector))::float8 AS "Similarity",
                   'semantic' AS "MatchKind"
            FROM ticket_embeddings e
            JOIN tickets t ON t.id = e.ticket_id
            WHERE t.status IN (4, 5)
              AND t.resolution IS NOT NULL AND t.resolution <> ''
              AND (1 - (e.embedding <=> @q::vector)) >= @minSimilarity
            ORDER BY e.embedding <=> @q::vector
            LIMIT @limit;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<ResolutionSuggestion>(sql, new
        {
            q = ToVectorLiteral(queryEmbedding),
            minSimilarity,
            limit = Math.Clamp(limit, 1, 50),
        })).ToList();
    }

    public async Task<IReadOnlyList<EmbeddingCandidate>> ListStaleAsync(int batchSize, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct)) return [];

        const string sql = $"""
            SELECT t.id AS "TicketId", {ContentExpr} AS "Content", {HashExpr} AS "ContentHash"
            FROM tickets t
            LEFT JOIN ticket_embeddings e ON e.ticket_id = t.id
            WHERE t.status IN (4, 5)
              AND t.resolution IS NOT NULL AND t.resolution <> ''
              AND (e.ticket_id IS NULL OR e.content_hash <> {HashExpr})
            ORDER BY t.id
            LIMIT @batchSize;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<EmbeddingCandidate>(sql, new { batchSize = Math.Clamp(batchSize, 1, 500) })).ToList();
    }

    public async Task<IndexStats> StatsAsync(CancellationToken ct = default)
    {
        var available = await IsAvailableAsync(ct);
        const string indexableSql = """
            SELECT count(*) FROM tickets
            WHERE status IN (4, 5) AND resolution IS NOT NULL AND resolution <> '';
            """;
        try
        {
            await using var conn = factory.Create();
            await conn.OpenAsync(ct);
            var indexable = await conn.ExecuteScalarAsync<long>(indexableSql);
            var indexed = available
                ? await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM ticket_embeddings")
                : 0L;
            return new IndexStats(indexed, indexable, available);
        }
        catch
        {
            // This feeds the diagnostics endpoint, which is exactly where an operator looks when the
            // database is down — reporting zeroes is more useful there than a 500.
            return new IndexStats(0, 0, false);
        }
    }

    /// <summary>
    /// pgvector's text input form. Round-trip ("R") formatting keeps the float exact, and the
    /// invariant culture guarantees a dot decimal separator whatever the host locale is.
    /// </summary>
    internal static string ToVectorLiteral(float[] v) =>
        string.Create(CultureInfo.InvariantCulture, $"[{string.Join(',', v.Select(f => f.ToString("R", CultureInfo.InvariantCulture)))}]");
}
