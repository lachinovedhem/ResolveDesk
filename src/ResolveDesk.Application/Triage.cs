using ResolveDesk.Core;

namespace ResolveDesk.Application;

/// <summary>Reads the ticket and says how hard, how long, what kind, and whether we have seen it already.</summary>
public interface IAssessmentService
{
    /// <summary>Runs the model and stores the result. Returns null when no chat model is configured.</summary>
    Task<TicketAssessment?> AssessAsync(long ticketId, CancellationToken ct = default);
    Task<TicketAssessment?> GetAsync(long ticketId, CancellationToken ct = default);
}

/// <summary>Tidies a written resolution and compares it with how the team solved the same thing before.</summary>
public interface IResolutionReviewService
{
    Task<ResolutionReview?> ReviewAsync(long ticketId, string resolution, CancellationToken ct = default);
    Task<ResolutionReview?> GetAsync(long ticketId, CancellationToken ct = default);
}

/// <summary>
/// Recommends who should take a ticket.
///
/// Deliberately arithmetic, not a model: the inputs are who actually resolved similar tickets, whose
/// declared skills overlap the subject, and who is already loaded. A coordinator can check every one
/// of those numbers, which is what makes the recommendation something they can overrule with reason
/// rather than a verdict they have to trust. The model's contribution is upstream — it supplies the
/// difficulty and category that feed in here.
/// </summary>
public interface IRoutingService
{
    /// <summary>
    /// The stored recommendation, or null if this ticket has never been analysed. Reading is a single
    /// row: the analysis behind it costs an embedding call and several queries, which is far too much
    /// to repeat every time a coordinator opens a ticket.
    /// </summary>
    Task<RoutingRecommendation?> GetAsync(long ticketId, CancellationToken ct = default);

    /// <summary>
    /// Runs the analysis and stores the result, replacing any previous one. Called on first view and
    /// whenever a coordinator asks for a fresh answer — after the team changes, or the ticket is
    /// edited, or more similar tickets have been resolved since.
    /// </summary>
    Task<RoutingRecommendation> RecommendAsync(long ticketId, CancellationToken ct = default);
}

public sealed record RoutingRecommendation(
    long? AssigneeId,
    string? AssigneeName,
    string Reason,
    IReadOnlyList<RoutingCandidate> Candidates,
    /// <summary>When the analysis ran. Shown in the UI so a stale answer is visibly stale.</summary>
    DateTime GeneratedAtUtc);

/// <summary>
/// One person's fit, with every input that produced the score kept alongside it — the point is that
/// the coordinator can see *why* someone came top.
/// </summary>
public sealed record RoutingCandidate(
    long UserId,
    string FullName,
    string? Skills,
    /// <summary>Tickets currently assigned and not finished.</summary>
    int OpenTickets,
    /// <summary>How many of the retrieved similar tickets this person resolved.</summary>
    int SolvedSimilar,
    /// <summary>Configured skill words that appear in the ticket text.</summary>
    int SkillMatches,
    /// <summary>0…1, higher is a better fit.</summary>
    double Score,
    string Reason);

public interface INotificationRepository
{
    Task<long> AddAsync(long userId, NotificationKind kind, long? ticketId, string title, string body,
        CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> ListAsync(long userId, bool unreadOnly, int limit, CancellationToken ct = default);
    Task<int> UnreadCountAsync(long userId, CancellationToken ct = default);
    Task MarkReadAsync(long userId, long notificationId, CancellationToken ct = default);
    Task MarkAllReadAsync(long userId, CancellationToken ct = default);
}

/// <summary>
/// In-process fan-out to connected SSE clients. Notifications are persisted first and delivered
/// second, so a user who was offline still finds them in the bell when they come back.
/// </summary>
public interface INotificationHub
{
    void Publish(Notification notification);
    IAsyncEnumerable<Notification> SubscribeAsync(long userId, CancellationToken ct);
}

/// <summary>Creates a notification, persists it, and pushes it to anyone listening.</summary>
public interface INotifier
{
    Task NotifyAsync(long userId, NotificationKind kind, long? ticketId, string title, string body,
        CancellationToken ct = default);
}

/// <summary>
/// Queues the model work a request must not wait for.
///
/// Creating a ticket and resolving one both trigger a model call, and on a local 7B model that is
/// seconds, not milliseconds. Neither belongs on the request path — the operator is on the phone with
/// a customer. Queuing also makes the model call something that can fail without failing the write
/// that caused it.
/// </summary>
public interface ITriageQueue
{
    void EnqueueAssessment(long ticketId);
    void EnqueueResolutionReview(long ticketId, string resolution);
}
