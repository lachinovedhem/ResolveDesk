using Dapper;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

public sealed class TicketRepository(DbConnectionFactory factory, SearchOptions search) : ITicketRepository
{
    // Shared column projection — quoted aliases match the Ticket record property names exactly.
    private const string Cols =
        @"id AS ""Id"", reference AS ""Reference"", title AS ""Title"", description AS ""Description"",
          status AS ""Status"", priority AS ""Priority"", source AS ""Source"", category AS ""Category"",
          customer_name AS ""CustomerName"", customer_contact AS ""CustomerContact"",
          assignee_id AS ""AssigneeId"", coordinator_id AS ""CoordinatorId"", resolution AS ""Resolution"",
          created_at_utc AS ""CreatedAtUtc"", updated_at_utc AS ""UpdatedAtUtc"",
          sla_due_at_utc AS ""SlaDueAtUtc"", resolved_at_utc AS ""ResolvedAtUtc""";

    public async Task<long> CreateAsync(TicketCreate input, DateTime? slaDueAtUtc, CancellationToken ct = default)
    {
        // Reference (RD-YYYY-000123) generated from a DB sequence in one round trip.
        const string sql = """
            INSERT INTO tickets (reference, title, description, status, priority, source, category, customer_name, customer_contact, sla_due_at_utc)
            VALUES ('RD-' || to_char(now(),'YYYY') || '-' || lpad(nextval('ticket_ref_seq')::text, 6, '0'),
                    @title, @description, 0, @priority, @source, @category, @customerName, @customerContact, @slaDueAtUtc)
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql, new {
            title = input.Title, description = input.Description,
            priority = (int)input.Priority, source = (int)input.Source, category = input.Category,
            customerName = input.CustomerName, customerContact = input.CustomerContact, slaDueAtUtc });
    }

    public async Task<Ticket?> GetAsync(long id, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM tickets WHERE id = @id";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Ticket>(sql, new { id });
    }

    public async Task<Ticket?> GetByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM tickets WHERE reference = @reference";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Ticket>(sql, new { reference });
    }

    public async Task<Page<Ticket>> ListAsync(TicketFilter f, CancellationToken ct = default)
    {
        // Keyset pagination (id DESC). AOT-safe: single const-shaped SQL, null-guarded filters.
        var sql = $"""
            SELECT {Cols} FROM tickets
            WHERE (@status IS NULL OR status = @status)
              AND (@priority IS NULL OR priority = @priority)
              AND (@assigneeId IS NULL OR assignee_id = @assigneeId)
              AND (@coordinatorId IS NULL OR coordinator_id = @coordinatorId)
              AND (@search IS NULL OR title ILIKE @search OR description ILIKE @search OR reference ILIKE @search)
              AND (@cursor IS NULL OR id < @cursor)
            ORDER BY id DESC
            LIMIT @limit
            """;
        var take = Math.Clamp(f.Limit, 1, 200);
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        var rows = (await conn.QueryAsync<Ticket>(sql, new {
            status = (int?)f.Status, priority = (int?)f.Priority,
            assigneeId = f.AssigneeId, coordinatorId = f.CoordinatorId,
            search = f.Search is null ? null : $"%{f.Search}%",
            cursor = f.Cursor, limit = take + 1 })).ToList();
        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new Page<Ticket>(rows, hasMore ? rows[^1].Id : null, hasMore);
    }

    public async Task<bool> AssignAsync(long ticketId, long assigneeId, long coordinatorId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tickets SET assignee_id = @assigneeId, coordinator_id = @coordinatorId,
                   status = CASE WHEN status = 0 THEN 1 ELSE status END, updated_at_utc = now()
            WHERE id = @ticketId;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(sql, new { ticketId, assigneeId, coordinatorId }) > 0;
    }

    public async Task<bool> SetStatusAsync(long ticketId, TicketStatus status, string? resolution, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tickets SET status = @status, updated_at_utc = now(),
                   resolution = COALESCE(@resolution, resolution),
                   resolved_at_utc = CASE WHEN @status IN (4,5) THEN now() ELSE resolved_at_utc END
            WHERE id = @ticketId;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(sql, new { ticketId, status = (int)status, resolution }) > 0;
    }

    public async Task<long> CountByStatusAsync(TicketStatus status, CancellationToken ct = default)
    {
        const string sql = "SELECT count(*) FROM tickets WHERE status = @status";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql, new { status = (int)status });
    }

    public async IAsyncEnumerable<Ticket> StreamResolvedAsync(long afterId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM tickets WHERE status IN (4,5) AND id > @afterId ORDER BY id";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        foreach (var t in await conn.QueryAsync<Ticket>(sql, new { afterId }))
            yield return t;
    }

    public async Task<IReadOnlyList<ResolutionSuggestion>> SearchResolvedByTextAsync(string query, int limit, CancellationToken ct = default)
    {
        // Lexical half of the hybrid search: always available, no extension needed, and the sole path
        // when embeddings are switched off. The semantic half lives in PgVectorIndex.
        //
        // The query terms are OR-ed, not AND-ed. `plainto_tsquery` builds an AND of every term, which
        // for a whole ticket body means a candidate must contain *all* thirty-odd words — that matched
        // nothing at all. Running the text through `to_tsvector` first and re-joining its lexemes with
        // `|` gives "any of these terms, ranked by how many and how close", which is what similarity
        // search actually wants. It also sanitises: whatever the customer typed comes back as lexemes,
        // never as tsquery operators.
        //
        // NULLIF guards the all-stopwords case: to_tsquery('') raises a syntax error, whereas NULL
        // makes `@@` return NULL and the row simply drops out.
        //
        // Normalisation 36 = 4 | 32, measured rather than assumed. Flag 4 divides by the mean harmonic
        // distance between term occurrences, which rewards a document where the query terms appear
        // near each other; flag 32 maps the result to [0,1) as rank/(rank+1), monotonically, so the
        // ordering flag 4 produced survives. Against the demo archive, 4 and 36 both put the right
        // ticket first for 7 of 7 queries; 0, 1, 2, 8, 16, 32 and 33 managed only 5 or 6.
        //
        // The value it returns is a RELEVANCE score, not a similarity: 0.04 can be a decisive first
        // place. It is not comparable with the cosine similarity a vector match reports, which is
        // exactly why the two lists are fused by rank (RRF) instead of by score — see ADR-003.
        const string sql = """
            WITH q AS (
                SELECT to_tsquery(@cfg::regconfig,
                           NULLIF(array_to_string(
                               tsvector_to_array(to_tsvector(@cfg::regconfig, @plain)), ' | '), '')) AS tsq
            )
            SELECT t.id AS "SourceTicketId", t.reference AS "SourceReference", t.title AS "Title",
                   COALESCE(t.resolution, '') AS "Resolution",
                   ts_rank_cd(t.search_tsv, q.tsq, 36)::float8 AS "Similarity",
                   'keyword' AS "MatchKind"
            FROM tickets t, q
            WHERE t.status IN (4,5) AND t.resolution IS NOT NULL AND t.resolution <> ''
              AND t.search_tsv @@ q.tsq
            ORDER BY "Similarity" DESC, t.resolved_at_utc DESC
            LIMIT @limit;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<ResolutionSuggestion>(sql,
            new { cfg = search.TextSearchConfig, plain = query, limit = Math.Clamp(limit, 1, 20) })).ToList();
    }
}
