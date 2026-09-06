using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Ai;

/// <summary>
/// Embedding client for every supported provider. The vector width is declared in configuration
/// (<c>Ai:Embedding:Dimensions</c>) because the pgvector column is fixed-width — a mismatch is a
/// migration, not a runtime fallback, so it is reported loudly rather than silently padded.
/// </summary>
public sealed class AiEmbeddingClient(HttpClient http, AiOptions options) : IAiEmbeddingClient
{
    private readonly AiEndpointOptions _o = options.Embedding;

    public bool IsEnabled { get; } = options.EmbeddingEnabled;
    public string ProviderName => _o.Provider.ToString();
    public string Model => _o.Model;
    public int Dimensions => _o.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var vectors = await EmbedBatchAsync([text], ct);
        return vectors.Count > 0 ? vectors[0] : [];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!IsEnabled || texts.Count == 0) return [];

        var vectors = _o.Provider switch
        {
            AiProvider.Ollama => await OllamaAsync(texts, ct),
            AiProvider.Gemini => await GeminiAsync(texts, ct),
            _ => await OpenAiAsync(texts, ct),
        };

        foreach (var v in vectors)
        {
            if (v.Length != _o.Dimensions)
                throw new AiProviderException(
                    $"Model '{_o.Model}' returned {v.Length}-dimension vectors but Ai:Embedding:Dimensions is " +
                    $"{_o.Dimensions}. Fix the setting and re-run the embedding backfill.");
        }
        return vectors;
    }

    /// <summary>Reachability probe for <c>/api/v1/ai/status</c> — never throws.</summary>
    public Task<bool> ProbeAsync(CancellationToken ct = default) =>
        IsEnabled ? AiHttp.ProbeCapabilityAsync(http, _o, ct) : Task.FromResult(false);

    private async Task<IReadOnlyList<float[]>> OpenAiAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var response = await AiHttp.PostAsync(http, _o,
            AiHttp.Url(_o, "/v1/embeddings", "embeddings", "embedContent"),
            new OpenAiEmbeddingRequest(_o.Model, texts),
            AiJsonContext.Default.OpenAiEmbeddingRequest, AiJsonContext.Default.OpenAiEmbeddingResponse, ct);

        if (response?.Data is not { Count: > 0 } data) return [];
        // The API may reorder; `index` is authoritative.
        return [.. data.OrderBy(d => d.Index).Select(d => d.Embedding ?? [])];
    }

    private async Task<IReadOnlyList<float[]>> OllamaAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var response = await AiHttp.PostAsync(http, _o, $"{AiHttp.Root(_o)}/api/embed",
            new OllamaEmbedRequest(_o.Model, texts),
            AiJsonContext.Default.OllamaEmbedRequest, AiJsonContext.Default.OllamaEmbedResponse, ct);

        return response?.Embeddings ?? [];
    }

    private async Task<IReadOnlyList<float[]>> GeminiAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var model = _o.Model.StartsWith("models/", StringComparison.Ordinal) ? _o.Model : $"models/{_o.Model}";
        var requests = texts
            .Select(t => new GeminiEmbedRequest(model, new GeminiContent([new GeminiPart(t)], null)))
            .ToArray();

        var response = await AiHttp.PostAsync(http, _o,
            AiHttp.Url(_o, "", "", "batchEmbedContents"),
            new GeminiBatchEmbedRequest(requests),
            AiJsonContext.Default.GeminiBatchEmbedRequest, AiJsonContext.Default.GeminiBatchEmbedResponse, ct);

        return response?.Embeddings is { } e ? [.. e.Select(x => x.Values ?? [])] : [];
    }
}

/// <summary>Stand-in used when <c>Ai:Enabled</c> is false or the embedding provider is None.</summary>
public sealed class DisabledEmbeddingClient : IAiEmbeddingClient
{
    public bool IsEnabled => false;
    public string ProviderName => nameof(AiProvider.None);
    public string Model => "";
    public int Dimensions => 0;
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult<float[]>([]);
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>([]);
}
