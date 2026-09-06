namespace ResolveDesk.Core;

/// <summary>
/// What the model concluded about an incoming ticket before anyone has looked at it: how hard it
/// looks, how long it is likely to take, where it belongs, and whether it is a repeat of something
/// already in the queue.
///
/// Advisory only. Nothing here changes the ticket by itself — a coordinator sees it beside their own
/// judgement, which is why <see cref="Confidence"/> is part of the record rather than hidden.
/// </summary>
public sealed record TicketAssessment(
    long TicketId,
    /// <summary>1 (routine, scripted) … 5 (needs escalation or field work).</summary>
    int Difficulty,
    /// <summary>Hands-on minutes, excluding waiting on the customer.</summary>
    int EstimatedMinutes,
    string? SuggestedCategory,
    TicketPriority? SuggestedPriority,
    /// <summary>Set when this looks like a repeat of an open ticket — the duplicate's id.</summary>
    long? DuplicateOfTicketId,
    string? DuplicateReference,
    /// <summary>One-sentence restatement of the problem, for queue scanning.</summary>
    string Summary,
    /// <summary>0…1. Low confidence is shown, not hidden — it is the signal to ignore the rest.</summary>
    double Confidence,
    string Model,
    DateTime CreatedAtUtc);

/// <summary>How a written resolution compares with how the team handled the same thing before.</summary>
public enum ConsistencyVerdict
{
    /// <summary>Matches established practice.</summary>
    Consistent = 0,
    /// <summary>Reaches the same end by a different route — worth a look, not necessarily wrong.</summary>
    Differs = 1,
    /// <summary>Nothing comparable in the archive; this resolution is new knowledge.</summary>
    Novel = 2,
    /// <summary>Contradicts a previous resolution — the one case a coordinator should read.</summary>
    Conflicts = 3,
}

/// <summary>
/// Produced when an agent resolves a ticket: a tidy version of what they wrote, plus how it lines up
/// with past resolutions of similar tickets.
///
/// The agent's own text is never replaced — <see cref="PolishedReply"/> sits alongside it, and the
/// ticket's stored resolution stays exactly what the human typed.
/// </summary>
public sealed record ResolutionReview(
    long TicketId,
    /// <summary>The agent's notes rewritten as something that can be sent to the customer.</summary>
    string PolishedReply,
    /// <summary>Tidy internal summary — what was wrong, what fixed it, what to watch for.</summary>
    string InternalNote,
    ConsistencyVerdict Verdict,
    string VerdictDetail,
    /// <summary>References of the past tickets this was compared against.</summary>
    string ComparedReferences,
    string Model,
    DateTime CreatedAtUtc);

public enum NotificationKind
{
    Assigned = 0,
    StatusChanged = 1,
    Comment = 2,
    SlaRisk = 3,
    ResolutionReviewed = 4,
    DuplicateDetected = 5,
}

public sealed record Notification(
    long Id,
    long UserId,
    NotificationKind Kind,
    long? TicketId,
    string Title,
    string Body,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);
