using ResolveDesk.Core;

namespace ResolveDesk.Application;

/// <summary>
/// Vector store over resolved tickets. Every method degrades quietly when pgvector is absent, so the
/// product keeps working on a plain PostgreSQL instance — just with keyword-only matching.
/// </summary>
public interface ISemanticIndex
{
    /// <summary>True when the <c>vector</c> extension is installed and the index table exists.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Creates the extension, table and HNSW index at the configured vector width. Idempotent.</summary>
    Task<bool> EnsureSchemaAsync(int dimensions, CancellationToken ct = default);

    Task UpsertAsync(long ticketId, string contentHash, float[] embedding, string model, CancellationToken ct = default);

    /// <summary>Nearest resolved tickets by cosine similarity, already filtered by <paramref name="minSimilarity"/>.</summary>
    Task<IReadOnlyList<ResolutionSuggestion>> SearchAsync(
        float[] queryEmbedding, int limit, double minSimilarity, CancellationToken ct = default);

    /// <summary>Resolved tickets whose embedding is missing or out of date — the backfill work queue.</summary>
    Task<IReadOnlyList<EmbeddingCandidate>> ListStaleAsync(int batchSize, CancellationToken ct = default);

    Task<IndexStats> StatsAsync(CancellationToken ct = default);
}

/// <summary>One unit of backfill work: the text to embed plus the hash that marks it current.</summary>
public sealed record EmbeddingCandidate(long TicketId, string Content, string ContentHash);

public sealed record IndexStats(long Indexed, long Indexable, bool VectorAvailable);

/// <summary>
/// The product's headline feature: given a new ticket, find what the team did about similar ones
/// before, and let the model turn those into a concrete proposed answer.
/// </summary>
public interface ISuggestionService
{
    Task<SuggestionResult> SuggestForTicketAsync(
        long ticketId, int limit, bool draftAnswer = true, CancellationToken ct = default);
    /// <summary>
    /// Finds past resolutions for arbitrary text. Set <paramref name="draftAnswer"/> to false when the
    /// caller only needs the matches — drafting is a second, far slower model call, and triage uses
    /// these matches purely to calibrate an effort estimate.
    /// </summary>
    Task<SuggestionResult> SuggestForTextAsync(
        string title, string description, int limit, bool draftAnswer = true, CancellationToken ct = default);
}

/// <summary>
/// Retrieval plus (optionally) a generated answer. <paramref name="Strategy"/> reports what actually
/// ran so the UI can be honest about whether the AI was involved.
/// </summary>
public sealed record SuggestionResult(
    IReadOnlyList<ResolutionSuggestion> Matches,
    string? Answer,
    string Strategy,      // hybrid | semantic | keyword | none
    string? Model,
    int Considered,
    long ElapsedMs);
