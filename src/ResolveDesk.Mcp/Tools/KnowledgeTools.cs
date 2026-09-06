using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using ResolveDesk.Application;

namespace ResolveDesk.Mcp.Tools;

/// <summary>
/// The reason this server exists: let an agent reach the team's own resolved-ticket history before it
/// answers anything. Tools return prose rather than raw JSON because the caller is a language model,
/// and a readable digest costs fewer tokens than a serialized object graph.
/// </summary>
[McpServerToolType]
public static class KnowledgeTools
{
    [McpServerTool(Name = "search_resolutions")]
    [Description("""
        Search how this team resolved similar tickets in the past. Hybrid retrieval: pgvector semantic
        similarity fused with PostgreSQL full-text search, plus an optional model-drafted answer.
        Use this FIRST for any 'how do we fix X' or 'customer reports Y' question — it returns what the
        team actually did, with ticket references, instead of a generic answer.
        """)]
    public static async Task<string> SearchResolutionsAsync(
        ResolveDeskClient client,
        [Description("The problem as the customer described it — a full sentence retrieves better than keywords.")] string problem,
        [Description("Extra context: error text, product area, what was already tried.")] string? details = null,
        [Description("How many past resolutions to return (1-20, default 5).")] int limit = 5,
        CancellationToken ct = default)
    {
        var result = await client.SearchResolutionsAsync(problem, details, Math.Clamp(limit, 1, 20), ct);
        return Render(result, problem);
    }

    [McpServerTool(Name = "suggest_for_ticket")]
    [Description("""
        Same knowledge lookup as search_resolutions, but for a ticket that already exists — the ticket's
        own title and description form the query, and the ticket itself is excluded from the matches.
        """)]
    public static async Task<string> SuggestForTicketAsync(
        ResolveDeskClient client,
        [Description("Numeric ticket id.")] long ticketId,
        [Description("How many past resolutions to return (1-20, default 5).")] int limit = 5,
        CancellationToken ct = default)
    {
        var result = await client.SuggestionsForTicketAsync(ticketId, Math.Clamp(limit, 1, 20), ct);
        return Render(result, $"ticket #{ticketId}");
    }

    [McpServerTool(Name = "ai_status")]
    [Description("""
        Report which AI providers and models this ResolveDesk instance is configured with, whether they
        are reachable, and how much of the resolved-ticket history has been indexed for semantic search.
        Call this when retrieval returns nothing, to tell a misconfiguration apart from an empty archive.
        """)]
    public static async Task<string> AiStatusAsync(ResolveDeskClient client, CancellationToken ct = default)
    {
        var s = await client.AiStatusAsync(ct);
        if (s is null) return "AI status unavailable.";
        return $"""
            AI enabled: {s.Enabled}
            Chat:       {s.ChatProvider} / {s.ChatModel} — {(s.ChatReachable ? "reachable" : "NOT reachable")}
            Embedding:  {s.EmbeddingProvider} / {s.EmbeddingModel} ({s.EmbeddingDimensions}-d) — {(s.EmbeddingReachable ? "reachable" : "NOT reachable")}
            Vector search: {(s.VectorSearchAvailable ? "available" : "unavailable — keyword only")}
            {s.Detail}
            """;
    }

    /// <summary>Digest of a retrieval result, ordered so the model reads the evidence before the draft.</summary>
    internal static string Render(SuggestionResult? result, string query)
    {
        if (result is null || result.Matches.Count == 0)
            return $"No past resolution matched \"{query}\". This looks like a new class of problem — " +
                   "resolve it manually, and the resolution becomes searchable for the next one.";

        var sb = new StringBuilder();
        sb.Append("Retrieval: ").Append(result.Strategy)
          .Append(" · ").Append(result.Matches.Count).Append(" match(es) from ").Append(result.Considered)
          .Append(" candidate(s) in ").Append(result.ElapsedMs).AppendLine("ms");
        sb.AppendLine();

        foreach (var m in result.Matches)
        {
            sb.Append("## ").Append(m.SourceReference).Append(" — ").AppendLine(m.Title);
            sb.Append("similarity ").Append(m.Similarity.ToString("0.00"))
              .Append(" · matched by ").AppendLine(m.MatchKind);
            sb.AppendLine(m.Resolution).AppendLine();
        }

        if (result.Answer is { Length: > 0 })
        {
            sb.Append("## Drafted answer (").Append(result.Model).AppendLine(")");
            sb.AppendLine("Generated from the resolutions above — verify before sending to a customer.");
            sb.AppendLine(result.Answer);
        }

        return sb.ToString();
    }
}
