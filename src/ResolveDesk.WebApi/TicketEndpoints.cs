using Microsoft.AspNetCore.Http.HttpResults;
using ResolveDesk.Application;
using ResolveDesk.Core;
using ResolveDesk.Infrastructure.Ai;

namespace ResolveDesk.WebApi;

public static class TicketEndpoints
{
    // SLA target by priority (standards 04: UTC everywhere).
    private static DateTime SlaFor(TicketPriority p) => DateTime.UtcNow + p switch {
        TicketPriority.Urgent => TimeSpan.FromHours(4),
        TicketPriority.High   => TimeSpan.FromHours(8),
        TicketPriority.Normal => TimeSpan.FromHours(24),
        _                     => TimeSpan.FromHours(72),
    };

    /// <summary>
    /// Maps the ticket surface. Authorization is applied per group rather than per endpoint so a new
    /// endpoint inherits the group's protection instead of silently shipping open.
    /// </summary>
    public static void MapApi(this WebApplication app, bool requireAuth)
    {
        var t = app.MapGroup("/api/v1/tickets");
        if (requireAuth) t.RequireAuthorization();

        t.MapPost("/", async (TicketCreate body, ITicketRepository repo, IActivityRepository acts,
            ITriageQueue triage, CancellationToken ct) =>
        {
            var id = await repo.CreateAsync(body, SlaFor(body.Priority), ct);
            await acts.AddAsync(id, null, ActivityKind.Comment, $"Ticket created from {body.Source}.", ct);
            // Sizing the ticket is a model call; the operator is on the phone, so it happens after
            // the response, not before it.
            triage.EnqueueAssessment(id);
            var created = await repo.GetAsync(id, ct);
            return Results.Created($"/api/v1/tickets/{id}", created);
        });

        t.MapGet("/", async (ITicketRepository repo, CancellationToken ct,
            TicketStatus? status, TicketPriority? priority, long? assigneeId, long? coordinatorId,
            string? search, long? cursor, int? limit) =>
            Results.Ok(await repo.ListAsync(new TicketFilter(status, priority, assigneeId, coordinatorId, search, cursor, limit ?? 50), ct)));

        t.MapGet("/{id:long}", async Task<Results<Ok<Ticket>, NotFound>> (long id, ITicketRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) is { } tk ? TypedResults.Ok(tk) : TypedResults.NotFound());

        // Routing tickets is the coordinator's job — the one role gate the domain actually needs.
        var assign = t.MapPost("/{id:long}/assign", async (long id, AssignRequest body, ITicketRepository repo,
            IActivityRepository acts, INotifier notifier, CancellationToken ct) =>
        {
            if (!await repo.AssignAsync(id, body.AssigneeId, body.CoordinatorId, ct)) return Results.NotFound();
            await acts.AddAsync(id, body.CoordinatorId, ActivityKind.Assignment, $"Assigned to agent #{body.AssigneeId}.", ct);

            var ticket = await repo.GetAsync(id, ct);
            await notifier.NotifyAsync(body.AssigneeId, NotificationKind.Assigned, id,
                $"{ticket?.Reference}: assigned to you", ticket?.Title ?? "", ct);
            return Results.NoContent();
        });
        if (requireAuth) assign.RequireAuthorization("coordinator");

        t.MapPost("/{id:long}/status", async (long id, StatusRequest body, ITicketRepository repo,
            IActivityRepository acts, ITriageQueue triage, INotifier notifier, CancellationToken ct) =>
        {
            if (!await repo.SetStatusAsync(id, body.Status, body.Resolution, ct)) return Results.NotFound();
            var resolving = body.Status is TicketStatus.Resolved or TicketStatus.Closed;
            var kind = resolving ? ActivityKind.Resolution : ActivityKind.StatusChange;
            await acts.AddAsync(id, null, kind, body.Resolution ?? $"Status → {body.Status}.", ct);

            // A written resolution gets tidied and compared against past practice — in the background,
            // because the agent is finished and should not wait for a model.
            if (resolving && !string.IsNullOrWhiteSpace(body.Resolution) && body.SkipTriage != true)
                triage.EnqueueResolutionReview(id, body.Resolution);

            var ticket = await repo.GetAsync(id, ct);
            if (ticket?.AssigneeId is { } assignee && !resolving)
            {
                await notifier.NotifyAsync(assignee, NotificationKind.StatusChanged, id,
                    $"{ticket.Reference}: {body.Status}", ticket.Title, ct);
            }
            return Results.NoContent();
        });

        t.MapGet("/{id:long}/activities", async (long id, IActivityRepository acts, CancellationToken ct) =>
            Results.Ok(await acts.ListAsync(id, ct)));

        t.MapPost("/{id:long}/comments", async (long id, CommentRequest body, IActivityRepository acts,
            ITicketRepository repo, INotifier notifier, CancellationToken ct) =>
        {
            var aid = await acts.AddAsync(id, body.AuthorId, ActivityKind.Comment, body.Body, ct);

            // Tell the assignee, unless they are the one who wrote it.
            var ticket = await repo.GetAsync(id, ct);
            if (ticket?.AssigneeId is { } assignee && assignee != body.AuthorId)
            {
                await notifier.NotifyAsync(assignee, NotificationKind.Comment, id,
                    $"{ticket.Reference}: new comment", body.Body, ct);
            }
            return Results.Created($"/api/v1/tickets/{id}/activities", new IdResponse(aid));
        });

        // The product's headline feature: what did we do about tickets like this one before?
        // Hybrid retrieval (pgvector + full-text) with an optional model-drafted answer on top.
        t.MapGet("/{id:long}/suggestions", async Task<Results<Ok<SuggestionResult>, NotFound>> (
            long id, ISuggestionService suggestions, int? limit, CancellationToken ct) =>
        {
            try { return TypedResults.Ok(await suggestions.SuggestForTicketAsync(id, limit ?? 5, draftAnswer: true, ct)); }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        });

        var u = app.MapGroup("/api/v1/users");
        if (requireAuth) u.RequireAuthorization();

        u.MapGet("/", async (IUserRepository repo, CancellationToken ct, UserRole? role) =>
            Results.Ok(await repo.ListAsync(role, ct)));

        var createUser = u.MapPost("/", async (UserCreate body, IUserRepository repo, CancellationToken ct) =>
            Results.Created($"/api/v1/users", new IdResponse(await repo.CreateAsync(body, ct))));
        if (requireAuth) createUser.RequireAuthorization("admin");

        var k = app.MapGroup("/api/v1");
        if (requireAuth) k.RequireAuthorization();

        // Search the knowledge base directly — used by the "check before you answer" panel and by the
        // internal MCP server, so an agent can look things up without creating a ticket first.
        k.MapPost("/knowledge/search", async (
            KnowledgeSearchRequest body, ISuggestionService suggestions, CancellationToken ct) =>
            Results.Ok(await suggestions.SuggestForTextAsync(body.Title, body.Description ?? "", body.Limit ?? 5, draftAnswer: true, ct)));

        // Operational view of the AI configuration. Reports providers, models and reachability —
        // never the API key, which is why this can stay readable by operators.
        k.MapGet("/ai/status", async (
            AiOptions options, IAiChatClient chat, IAiEmbeddingClient embed,
            ISemanticIndex index, IServiceProvider sp, CancellationToken ct) =>
        {
            var stats = await index.StatsAsync(ct);
            var chatUp = sp.GetService<AiChatClient>() is { } c && await c.ProbeAsync(ct);
            var embedUp = sp.GetService<AiEmbeddingClient>() is { } e && await e.ProbeAsync(ct);

            var detail = (options.Enabled, stats.VectorAvailable) switch
            {
                (false, _) => "AI disabled — keyword search only. Set Ai:Enabled=true to turn it on.",
                (true, false) => "pgvector not available — keyword search only. Install the extension, then restart.",
                _ => $"{stats.Indexed}/{stats.Indexable} resolved tickets indexed.",
            };

            return Results.Ok(new AiStatus(
                options.Enabled,
                chat.ProviderName, chat.Model, chatUp,
                embed.ProviderName, embed.Model, embed.Dimensions, embedUp,
                stats.VectorAvailable, detail));
        });

        k.MapGet("/knowledge/stats", async (ISemanticIndex index, CancellationToken ct) =>
            Results.Ok(await index.StatsAsync(ct)));

        k.MapGet("/stats", async (ITicketRepository repo, CancellationToken ct) => Results.Ok(new StatsResponse(
            await repo.CountByStatusAsync(TicketStatus.Open, ct),
            await repo.CountByStatusAsync(TicketStatus.Assigned, ct),
            await repo.CountByStatusAsync(TicketStatus.InProgress, ct),
            await repo.CountByStatusAsync(TicketStatus.WaitingCustomer, ct),
            await repo.CountByStatusAsync(TicketStatus.Resolved, ct),
            await repo.CountByStatusAsync(TicketStatus.Closed, ct))));
    }
}
