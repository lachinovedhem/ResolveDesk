using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using ResolveDesk.Core;

namespace ResolveDesk.Mcp.Tools;

/// <summary>Read and light-write access to the ticket queue itself.</summary>
[McpServerToolType]
public static class TicketTools
{
    [McpServerTool(Name = "get_ticket")]
    [Description("Fetch one ticket by numeric id: status, priority, customer, assignee, SLA and resolution.")]
    public static async Task<string> GetTicketAsync(
        ResolveDeskClient client,
        [Description("Numeric ticket id.")] long ticketId,
        CancellationToken ct = default)
    {
        var t = await client.GetTicketAsync(ticketId, ct);
        return t is null ? $"Ticket {ticketId} not found." : Describe(t);
    }

    [McpServerTool(Name = "list_tickets")]
    [Description("""
        List tickets in the queue, newest first. Filter by status and/or assignee.
        Statuses: Open, Assigned, InProgress, WaitingCustomer, Resolved, Closed.
        """)]
    public static async Task<string> ListTicketsAsync(
        ResolveDeskClient client,
        [Description("Status name, e.g. 'Open'. Omit for every status.")] string? status = null,
        [Description("Only tickets assigned to this user id.")] long? assigneeId = null,
        [Description("How many to return (1-100, default 20).")] int limit = 20,
        CancellationToken ct = default)
    {
        var page = await client.ListTicketsAsync(status, assigneeId, Math.Clamp(limit, 1, 100), ct);
        if (page is null || page.Items.Count == 0) return "No tickets matched.";

        var sb = new StringBuilder();
        sb.Append(page.Items.Count).AppendLine(page.HasMore ? " ticket(s) (more available):" : " ticket(s):");
        foreach (var t in page.Items)
            sb.Append("- #").Append(t.Id).Append(' ').Append(t.Reference)
              .Append(" [").Append(t.Status).Append('/').Append(t.Priority).Append("] ")
              .Append(t.Title)
              .Append(t.AssigneeId is { } a ? $" → agent #{a}" : " → unassigned")
              .AppendLine();
        return sb.ToString();
    }

    [McpServerTool(Name = "ticket_history")]
    [Description("The full activity timeline of a ticket: comments, status changes, assignments, resolutions.")]
    public static async Task<string> TicketHistoryAsync(
        ResolveDeskClient client,
        [Description("Numeric ticket id.")] long ticketId,
        CancellationToken ct = default)
    {
        var items = await client.ActivitiesAsync(ticketId, ct);
        if (items is null || items.Count == 0) return $"No activity recorded on ticket {ticketId}.";

        var sb = new StringBuilder();
        foreach (var a in items)
            sb.Append(a.CreatedAtUtc.ToString("u")).Append(" · ").Append(a.Kind)
              .Append(a.AuthorId is { } id ? $" · user #{id}" : " · system")
              .AppendLine().AppendLine(a.Body).AppendLine();
        return sb.ToString();
    }

    [McpServerTool(Name = "list_agents")]
    [Description("List the technical team: agents, coordinators and admins, with their skills — use this before recommending an assignee.")]
    public static async Task<string> ListAgentsAsync(
        ResolveDeskClient client,
        [Description("Filter by role: Agent, Coordinator or Admin. Omit for everyone.")] string? role = null,
        CancellationToken ct = default)
    {
        var users = await client.ListUsersAsync(role, ct);
        if (users is null || users.Count == 0) return "No users found.";

        var sb = new StringBuilder();
        foreach (var u in users)
            sb.Append("- #").Append(u.Id).Append(' ').Append(u.FullName)
              .Append(" [").Append(u.Role).Append(']')
              .Append(string.IsNullOrWhiteSpace(u.Skills) ? "" : $" — skills: {u.Skills}")
              .AppendLine();
        return sb.ToString();
    }

    [McpServerTool(Name = "queue_stats")]
    [Description("Ticket counts per status — the coordinator's view of current load.")]
    public static async Task<string> QueueStatsAsync(ResolveDeskClient client, CancellationToken ct = default)
    {
        var stats = await client.StatsAsync(ct);
        return stats.ToString();
    }

    [McpServerTool(Name = "add_comment")]
    [Description("""
        Append a comment to a ticket's timeline. This writes to the live system and is visible to the
        team, so state clearly what you are adding and why before calling it.
        """)]
    public static async Task<string> AddCommentAsync(
        ResolveDeskClient client,
        [Description("Numeric ticket id.")] long ticketId,
        [Description("Comment text.")] string body,
        [Description("Id of the user this comment is attributed to. Omit for a system comment.")] long? authorId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Refused: comment body is empty.";
        await client.AddCommentAsync(ticketId, authorId, body, ct);
        return $"Comment added to ticket {ticketId}.";
    }

    internal static string Describe(Ticket t) => $"""
        #{t.Id} {t.Reference} — {t.Title}
        Status: {t.Status} · Priority: {t.Priority} · Source: {t.Source} · Category: {t.Category ?? "—"}
        Customer: {t.CustomerName} ({t.CustomerContact ?? "no contact"})
        Assignee: {(t.AssigneeId is { } a ? $"#{a}" : "unassigned")} · Coordinator: {(t.CoordinatorId is { } c ? $"#{c}" : "—")}
        Created: {t.CreatedAtUtc:u} · SLA due: {(t.SlaDueAtUtc is { } d ? d.ToString("u") : "—")}
        Resolved: {(t.ResolvedAtUtc is { } r ? r.ToString("u") : "—")}

        Description:
        {t.Description}

        Resolution:
        {(string.IsNullOrWhiteSpace(t.Resolution) ? "— not resolved yet —" : t.Resolution)}
        """;
}
