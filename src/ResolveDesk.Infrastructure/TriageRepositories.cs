using Dapper;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// One stored routing analysis as it comes back from the database. The jsonb column arrives as text —
/// Dapper.AOT maps rows, not JSON — and is deserialised by the repository.
///
/// Declared here rather than nested inside the repository: Dapper.AOT's generated row factory cannot
/// name a type nested in the class it is generating for, and fails with "Type expected".
/// </summary>
public sealed record StoredRouting(
    long? AssigneeId, string? AssigneeName, string Reason, string Candidates, DateTime GeneratedAtUtc);

/// <summary>Persistence for model-produced assessments and resolution reviews.</summary>
public sealed class TriageRepository(DbConnectionFactory factory)
{
    private const string AssessmentCols =
        @"ticket_id AS ""TicketId"", difficulty AS ""Difficulty"", estimated_minutes AS ""EstimatedMinutes"",
          suggested_category AS ""SuggestedCategory"", suggested_priority AS ""SuggestedPriority"",
          duplicate_of_ticket_id AS ""DuplicateOfTicketId"", summary AS ""Summary"",
          confidence AS ""Confidence"", model AS ""Model"", created_at_utc AS ""CreatedAtUtc""";

    private const string ReviewCols =
        @"ticket_id AS ""TicketId"", polished_reply AS ""PolishedReply"", internal_note AS ""InternalNote"",
          verdict AS ""Verdict"", verdict_detail AS ""VerdictDetail"",
          compared_references AS ""ComparedReferences"", model AS ""Model"", created_at_utc AS ""CreatedAtUtc""";

    public async Task SaveAssessmentAsync(TicketAssessment a, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ticket_assessments
                (ticket_id, difficulty, estimated_minutes, suggested_category, suggested_priority,
                 duplicate_of_ticket_id, summary, confidence, model)
            VALUES (@ticketId, @difficulty, @estimatedMinutes, @suggestedCategory, @suggestedPriority,
                    @duplicateOfTicketId, @summary, @confidence, @model)
            ON CONFLICT (ticket_id) DO UPDATE SET
                difficulty = EXCLUDED.difficulty,
                estimated_minutes = EXCLUDED.estimated_minutes,
                suggested_category = EXCLUDED.suggested_category,
                suggested_priority = EXCLUDED.suggested_priority,
                duplicate_of_ticket_id = EXCLUDED.duplicate_of_ticket_id,
                summary = EXCLUDED.summary,
                confidence = EXCLUDED.confidence,
                model = EXCLUDED.model,
                created_at_utc = now();
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            ticketId = a.TicketId,
            difficulty = a.Difficulty,
            estimatedMinutes = a.EstimatedMinutes,
            suggestedCategory = a.SuggestedCategory,
            suggestedPriority = (int?)a.SuggestedPriority,
            duplicateOfTicketId = a.DuplicateOfTicketId,
            summary = a.Summary,
            confidence = a.Confidence,
            model = a.Model,
        });
    }

    public async Task<TicketAssessment?> GetAssessmentAsync(long ticketId, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {AssessmentCols},
                   (SELECT reference FROM tickets d WHERE d.id = a.duplicate_of_ticket_id) AS "DuplicateReference"
            FROM ticket_assessments a WHERE ticket_id = @ticketId
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<TicketAssessment>(sql, new { ticketId });
    }

    public async Task SaveRoutingAsync(long ticketId, RoutingRecommendation r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ticket_routing (ticket_id, assignee_id, assignee_name, reason, candidates)
            VALUES (@ticketId, @assigneeId, @assigneeName, @reason, @candidates::jsonb)
            ON CONFLICT (ticket_id) DO UPDATE SET
                assignee_id = EXCLUDED.assignee_id,
                assignee_name = EXCLUDED.assignee_name,
                reason = EXCLUDED.reason,
                candidates = EXCLUDED.candidates,
                created_at_utc = now();
            """;
        // Serialised into a local first: Dapper.AOT infers each parameter's type from the anonymous
        // object, and an inline call it cannot resolve is emitted as an untyped `default()`.
        string candidates = System.Text.Json.JsonSerializer.Serialize(
            r.Candidates, TriageJsonContext.Default.IReadOnlyListRoutingCandidate);

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            ticketId,
            assigneeId = r.AssigneeId,
            assigneeName = r.AssigneeName,
            reason = r.Reason,
            candidates,
        });
    }

    public async Task<RoutingRecommendation?> GetRoutingAsync(long ticketId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT assignee_id AS "AssigneeId", assignee_name AS "AssigneeName", reason AS "Reason",
                   candidates::text AS "Candidates", created_at_utc AS "GeneratedAtUtc"
            FROM ticket_routing WHERE ticket_id = @ticketId
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<StoredRouting>(sql, new { ticketId });
        if (row is null) return null;

        var candidates = System.Text.Json.JsonSerializer.Deserialize(
            row.Candidates, TriageJsonContext.Default.IReadOnlyListRoutingCandidate) ?? [];
        return new RoutingRecommendation(
            row.AssigneeId, row.AssigneeName, row.Reason, candidates, row.GeneratedAtUtc);
    }

    public async Task SaveReviewAsync(ResolutionReview r, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO resolution_reviews
                (ticket_id, polished_reply, internal_note, verdict, verdict_detail, compared_references, model)
            VALUES (@ticketId, @polishedReply, @internalNote, @verdict, @verdictDetail, @comparedReferences, @model)
            ON CONFLICT (ticket_id) DO UPDATE SET
                polished_reply = EXCLUDED.polished_reply,
                internal_note = EXCLUDED.internal_note,
                verdict = EXCLUDED.verdict,
                verdict_detail = EXCLUDED.verdict_detail,
                compared_references = EXCLUDED.compared_references,
                model = EXCLUDED.model,
                created_at_utc = now();
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            ticketId = r.TicketId,
            polishedReply = r.PolishedReply,
            internalNote = r.InternalNote,
            verdict = (int)r.Verdict,
            verdictDetail = r.VerdictDetail,
            comparedReferences = r.ComparedReferences,
            model = r.Model,
        });
    }

    public async Task<ResolutionReview?> GetReviewAsync(long ticketId, CancellationToken ct = default)
    {
        var sql = $"SELECT {ReviewCols} FROM resolution_reviews WHERE ticket_id = @ticketId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<ResolutionReview>(sql, new { ticketId });
    }

    /// <summary>Open workload per agent — one of the routing inputs.</summary>
    public async Task<IReadOnlyList<AgentLoad>> LoadPerAgentAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT u.id AS "UserId", count(t.id) AS "OpenTickets"
            FROM users u
            LEFT JOIN tickets t ON t.assignee_id = u.id AND t.status NOT IN (4, 5)
            WHERE u.is_active AND u.role = 0
            GROUP BY u.id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<AgentLoad>(sql)).ToList();
    }

    /// <summary>Who resolved each of these tickets — the "has done this before" routing input.</summary>
    public async Task<IReadOnlyList<TicketAssignee>> AssigneesOfAsync(long[] ticketIds, CancellationToken ct = default)
    {
        if (ticketIds.Length == 0) return [];
        const string sql = """
            SELECT id AS "TicketId", assignee_id AS "AssigneeId"
            FROM tickets WHERE id = ANY(@ticketIds) AND assignee_id IS NOT NULL;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<TicketAssignee>(sql, new { ticketIds })).ToList();
    }

    /// <summary>
    /// Still-open tickets that could be the same problem. Two signals, ranked: the same customer
    /// calling again (the common real duplicate), then anything textually close from anyone —
    /// which catches an outage being reported by twenty people at once.
    /// </summary>
    public async Task<IReadOnlyList<OpenTicketBrief>> DuplicateCandidatesAsync(
        string customerName, string query, long excludeTicketId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", reference AS "Reference", title AS "Title",
                   customer_name AS "CustomerName", created_at_utc AS "CreatedAtUtc",
                   (lower(customer_name) = lower(@customerName)) AS "SameCustomer"
            FROM tickets
            WHERE status NOT IN (4, 5) AND id <> @excludeTicketId
              AND (lower(customer_name) = lower(@customerName)
                   OR to_tsvector('simple', title || ' ' || description)
                        @@ plainto_tsquery('simple', @query))
            ORDER BY (lower(customer_name) = lower(@customerName)) DESC, created_at_utc DESC
            LIMIT 8;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<OpenTicketBrief>(sql,
            new { customerName, query, excludeTicketId })).ToList();
    }

    /// <summary>
    /// Unfinished tickets whose SLA runs out soon and that nobody has been warned about yet.
    /// The "not yet warned" check is a NOT EXISTS against the notifications table rather than a flag
    /// column, so the warning is idempotent without adding state to the ticket.
    /// </summary>
    public async Task<IReadOnlyList<SlaRisk>> SlaRisksAsync(int withinMinutes, CancellationToken ct = default)
    {
        const string sql = """
            SELECT t.id AS "TicketId", t.reference AS "Reference", t.title AS "Title",
                   t.assignee_id AS "AssigneeId", t.sla_due_at_utc AS "SlaDueAtUtc"
            FROM tickets t
            WHERE t.status NOT IN (4, 5)
              AND t.sla_due_at_utc IS NOT NULL
              AND t.sla_due_at_utc <= now() + make_interval(mins => @withinMinutes)
              AND NOT EXISTS (
                    SELECT 1 FROM notifications n
                    WHERE n.ticket_id = t.id AND n.kind = 3)
            ORDER BY t.sla_due_at_utc
            LIMIT 50;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<SlaRisk>(sql, new { withinMinutes })).ToList();
    }

    public sealed record SlaRisk(
        long TicketId, string Reference, string Title, long? AssigneeId, DateTime SlaDueAtUtc);

    public sealed record AgentLoad(long UserId, int OpenTickets);
    public sealed record TicketAssignee(long TicketId, long AssigneeId);
    public sealed record OpenTicketBrief(
        long Id, string Reference, string Title, string CustomerName,
        DateTime CreatedAtUtc, bool SameCustomer);
}

public sealed class NotificationRepository(DbConnectionFactory factory) : INotificationRepository
{
    private const string Cols =
        @"id AS ""Id"", user_id AS ""UserId"", kind AS ""Kind"", ticket_id AS ""TicketId"",
          title AS ""Title"", body AS ""Body"", created_at_utc AS ""CreatedAtUtc"", read_at_utc AS ""ReadAtUtc""";

    public async Task<long> AddAsync(long userId, NotificationKind kind, long? ticketId, string title, string body,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notifications (user_id, kind, ticket_id, title, body)
            VALUES (@userId, @kind, @ticketId, @title, @body)
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql,
            new { userId, kind = (int)kind, ticketId, title, body });
    }

    public async Task<IReadOnlyList<Notification>> ListAsync(long userId, bool unreadOnly, int limit,
        CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Cols} FROM notifications
            WHERE user_id = @userId AND (@unreadOnly = false OR read_at_utc IS NULL)
            ORDER BY created_at_utc DESC
            LIMIT @limit
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<Notification>(sql,
            new { userId, unreadOnly, limit = Math.Clamp(limit, 1, 200) })).ToList();
    }

    public async Task<int> UnreadCountAsync(long userId, CancellationToken ct = default)
    {
        const string sql = "SELECT count(*) FROM notifications WHERE user_id = @userId AND read_at_utc IS NULL";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(sql, new { userId });
    }

    public async Task MarkReadAsync(long userId, long notificationId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE notifications SET read_at_utc = now()
            WHERE id = @notificationId AND user_id = @userId AND read_at_utc IS NULL;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { userId, notificationId });
    }

    public async Task MarkAllReadAsync(long userId, CancellationToken ct = default)
    {
        const string sql = "UPDATE notifications SET read_at_utc = now() WHERE user_id = @userId AND read_at_utc IS NULL";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { userId });
    }
}
