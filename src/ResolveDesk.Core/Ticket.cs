namespace ResolveDesk.Core;

/// <summary>
/// A support ticket coming into the technical team's call center. When resolved, its
/// Resolution text becomes part of the knowledge base the AI searches for future tickets.
/// </summary>
public sealed record Ticket(
    long Id,
    string Reference,          // human id, e.g. RD-2026-000123
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    TicketSource Source,
    string? Category,
    string CustomerName,
    string? CustomerContact,
    long? AssigneeId,          // agent
    long? CoordinatorId,       // who routed it
    string? Resolution,        // filled when Status = Resolved/Closed
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? SlaDueAtUtc,
    DateTime? ResolvedAtUtc);

/// <summary>Timeline entry: comment, status change, assignment, resolution, or AI suggestion.</summary>
public sealed record TicketActivity(
    long Id,
    long TicketId,
    long? AuthorId,            // null = system/AI
    ActivityKind Kind,
    string Body,
    DateTime CreatedAtUtc);

/// <summary>A potential answer surfaced from a similar past resolved ticket.</summary>
public sealed record ResolutionSuggestion(
    long SourceTicketId,
    string SourceReference,
    string Title,
    string Resolution,
    double Similarity,         // 0..1
    string MatchKind);         // semantic | keyword | hybrid — lets the UI show why a match surfaced
