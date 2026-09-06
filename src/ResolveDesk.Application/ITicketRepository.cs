using ResolveDesk.Core;

namespace ResolveDesk.Application;

public interface ITicketRepository
{
    Task<long> CreateAsync(TicketCreate input, DateTime? slaDueAtUtc, CancellationToken ct = default);
    Task<Ticket?> GetAsync(long id, CancellationToken ct = default);
    Task<Ticket?> GetByReferenceAsync(string reference, CancellationToken ct = default);
    Task<Page<Ticket>> ListAsync(TicketFilter filter, CancellationToken ct = default);
    Task<bool> AssignAsync(long ticketId, long assigneeId, long coordinatorId, CancellationToken ct = default);
    Task<bool> SetStatusAsync(long ticketId, TicketStatus status, string? resolution, CancellationToken ct = default);
    Task<long> CountByStatusAsync(TicketStatus status, CancellationToken ct = default);

    /// <summary>Resolved/closed tickets — the knowledge base for AI suggestions.</summary>
    IAsyncEnumerable<Ticket> StreamResolvedAsync(long afterId, CancellationToken ct = default);
    /// <summary>Keyword search over resolved tickets (fallback until semantic search is wired).</summary>
    Task<IReadOnlyList<ResolutionSuggestion>> SearchResolvedByTextAsync(string query, int limit, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<long> CreateAsync(UserCreate input, CancellationToken ct = default);
    Task<User?> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListAsync(UserRole? role, CancellationToken ct = default);
}

public interface IActivityRepository
{
    Task<long> AddAsync(long ticketId, long? authorId, ActivityKind kind, string body, CancellationToken ct = default);
    Task<IReadOnlyList<TicketActivity>> ListAsync(long ticketId, CancellationToken ct = default);
}
