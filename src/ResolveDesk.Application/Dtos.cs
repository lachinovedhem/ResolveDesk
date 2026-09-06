using ResolveDesk.Core;

namespace ResolveDesk.Application;

public sealed record TicketCreate(
    string Title, string Description, TicketPriority Priority, TicketSource Source,
    string? Category, string CustomerName, string? CustomerContact);

public sealed record TicketFilter(
    TicketStatus? Status = null, TicketPriority? Priority = null,
    long? AssigneeId = null, long? CoordinatorId = null, string? Search = null,
    long? Cursor = null, int Limit = 50);

public sealed record Page<T>(IReadOnlyList<T> Items, long? NextCursor, bool HasMore);

public sealed record UserCreate(string FullName, string Email, UserRole Role, string? Skills);
