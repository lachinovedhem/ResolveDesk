using Microsoft.Extensions.Configuration;

namespace ResolveDesk.Application;

/// <summary>
/// Which backend serves a given AI capability. Chosen per capability from configuration, so chat can
/// run on a hosted model while embeddings stay on a local one (or the reverse, or both local).
/// </summary>
public enum AiProvider
{
    /// <summary>Capability switched off — callers degrade to keyword-only behaviour.</summary>
    None = 0,
    /// <summary>Local Ollama daemon (native /api/chat, /api/embed).</summary>
    Ollama = 1,
    /// <summary>OpenAI, or anything that speaks its wire format: LM Studio, vLLM, llama.cpp, OpenRouter…</summary>
    OpenAi = 2,
    /// <summary>Azure OpenAI deployment (api-key header, deployment-scoped URL).</summary>
    AzureOpenAi = 3,
    /// <summary>Google Gemini generative-language API.</summary>
    Gemini = 4,
}

/// <summary>One configured capability endpoint (chat or embedding).</summary>
public sealed record AiEndpointOptions
{
    public AiProvider Provider { get; init; } = AiProvider.None;
    /// <summary>Root URL without a trailing slash, e.g. <c>http://localhost:11434</c>.</summary>
    public string BaseUrl { get; init; } = "";
    public string Model { get; init; } = "";
    /// <summary>Never hardcoded — comes from environment or user-secrets. Empty for local providers.</summary>
    public string? ApiKey { get; init; }
    /// <summary>Embedding vector width. Must match the pgvector column, so changing it needs a migration.</summary>
    public int Dimensions { get; init; } = 768;
    public int TimeoutSeconds { get; init; } = 120;
    /// <summary>Azure only: api-version query parameter.</summary>
    public string? ApiVersion { get; init; }

    public bool Enabled => Provider != AiProvider.None;
}

/// <summary>Root of the <c>Ai</c> configuration section.</summary>
public sealed record AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Master switch — off means the whole product still works, just without AI.</summary>
    public bool Enabled { get; init; }
    public AiEndpointOptions Chat { get; init; } = new();
    public AiEndpointOptions Embedding { get; init; } = new();

    /// <summary>
    /// Cosine similarity below which a retrieved ticket is discarded as noise.
    ///
    /// This is a **floor, not a separator**. Measured on nomic-embed-text against the demo archive
    /// (<c>tools/validate-retrieval.mjs</c>), true matches score 0.58–0.74 and unrelated pairs
    /// 0.33–0.63 — the two populations overlap, so no threshold can divide them. Ranking does the
    /// discrimination; this value only cuts the obvious floor:
    ///
    /// <code>
    ///   threshold   true kept   distractors through   headroom
    ///     0.40        7/7            57/63              0.182
    ///     0.45        7/7            42/63              0.132
    ///     0.50        7/7            18/63              0.082   ← default
    ///     0.55        7/7            12/63              0.032
    /// </code>
    ///
    /// 0.55 removes only six more distractors but halves the margin above the weakest true match,
    /// which is a poor trade on a seven-query sample. It is configurable because the scale belongs to
    /// the embedding model, not to this product — re-run the harness after changing models.
    /// </summary>
    public double MinSimilarity { get; init; } = 0.50;

    public bool ChatEnabled => Enabled && Chat.Enabled;
    public bool EmbeddingEnabled => Enabled && Embedding.Enabled;

    /// <summary>
    /// Reads the <c>Ai</c> section by explicit key. Reflection-based options binding is avoided so the
    /// API's configuration path stays Native-AOT safe; env vars still work through the usual
    /// <c>Ai__Chat__Provider</c> double-underscore form. The MCP sidecar binds through the same method,
    /// so both processes read one configuration shape.
    /// </summary>
    public static AiOptions Read(IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        return new AiOptions
        {
            Enabled = Bool(section["Enabled"], false),
            Chat = Endpoint(section.GetSection("Chat"), defaultDimensions: 0),
            Embedding = Endpoint(section.GetSection("Embedding"), defaultDimensions: 768),
            MinSimilarity = Double(section["MinSimilarity"], 0.50),
        };

        static AiEndpointOptions Endpoint(IConfiguration s, int defaultDimensions) => new()
        {
            Provider = Enum.TryParse<AiProvider>(s["Provider"], ignoreCase: true, out var p) ? p : AiProvider.None,
            BaseUrl = s["BaseUrl"] ?? "",
            Model = s["Model"] ?? "",
            // Keys belong in env vars or a secret store — never in a committed appsettings file.
            ApiKey = s["ApiKey"],
            Dimensions = Int(s["Dimensions"], defaultDimensions),
            TimeoutSeconds = Int(s["TimeoutSeconds"], 120),
            ApiVersion = s["ApiVersion"],
        };

        static bool Bool(string? v, bool fallback) => bool.TryParse(v, out var b) ? b : fallback;
        static int Int(string? v, int fallback) => int.TryParse(v, out var i) ? i : fallback;

        // Invariant culture explicitly: a machine with a comma decimal separator must still read
        // "0.55" from configuration as 0.55, not fail and silently fall back.
        static double Double(string? v, double fallback) =>
            double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? Math.Clamp(d, 0, 1)
                : fallback;
    }
}

/// <summary>Generates a natural-language answer. Implementations are stateless and thread-safe.</summary>
public interface IAiChatClient
{
    bool IsEnabled { get; }
    string ProviderName { get; }
    string Model { get; }

    /// <summary>Single-turn completion. Returns an empty string when the capability is disabled.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

/// <summary>Turns text into a vector for semantic search. Implementations are stateless and thread-safe.</summary>
public interface IAiEmbeddingClient
{
    bool IsEnabled { get; }
    string ProviderName { get; }
    string Model { get; }
    /// <summary>Vector width this client emits — must equal the pgvector column width.</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>Batched variant used by the backfill job; order matches the input.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>What <c>GET /api/v1/ai/status</c> reports — lets operators verify config without leaking keys.</summary>
public sealed record AiStatus(
    bool Enabled,
    string ChatProvider,
    string ChatModel,
    bool ChatReachable,
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    bool EmbeddingReachable,
    bool VectorSearchAvailable,
    string? Detail);
